using FluentAssertions;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

public sealed class SettlementRecordServiceTests
{
    private static async Task<(TestHarness Harness, Guid OwnerId, Guid GuestUserId, GroupDto Group, Guid OwnerMemberId, Guid GuestMemberId)> SetupGroupAsync()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var guestUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guestMember = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(guestUserId, "Binh"), CancellationToken.None);
        return (harness, ownerId, guestUserId, group, group.Members[0].Id, guestMember.Id);
    }

    [Fact]
    public async Task CreateAsync_CreatesPendingSettlement()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        settlement.Status.Should().Be("Pending");
        settlement.Amount.Should().Be(50_000);
    }

    [Fact]
    public async Task CreateAsync_SameFromAndTo_ThrowsSettlementSameMember()
    {
        var (harness, ownerId, _, group, ownerMemberId, _) = await SetupGroupAsync();

        var act = () => harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(ownerMemberId, ownerMemberId, 50_000), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.SettlementSameMember);
    }

    [Fact]
    public async Task ConfirmAsync_ByNonReceiver_ThrowsInsufficientRole()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        // Người GHI NHẬN (owner) không phải người NHẬN tiền thật -> chỉ ToMemberId (owner) mới đúng...
        // ở đây thử với guestUserId (không phải ToMemberId) để xác nhận bị chặn.
        var guestUserId = harness.DbContext.Users.Single(u => u.Email == "b@example.com").Id;
        var act = () => harness.SettlementRecordService.ConfirmAsync(guestUserId, settlement.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }

    [Fact]
    public async Task ConfirmAsync_ByReceiver_Succeeds()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        var confirmed = await harness.SettlementRecordService.ConfirmAsync(ownerId, settlement.Id, CancellationToken.None);

        confirmed.Status.Should().Be("Confirmed");
        confirmed.ConfirmedByMemberId.Should().Be(ownerMemberId);
    }

    [Fact]
    public async Task ConfirmAsync_AlreadyConfirmed_ThrowsSettlementNotPending()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);
        await harness.SettlementRecordService.ConfirmAsync(ownerId, settlement.Id, CancellationToken.None);

        var act = () => harness.SettlementRecordService.ConfirmAsync(ownerId, settlement.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.SettlementNotPending);
    }

    [Fact]
    public async Task RejectAsync_ByReceiver_Succeeds()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        var rejected = await harness.SettlementRecordService.RejectAsync(ownerId, settlement.Id, CancellationToken.None);

        rejected.Status.Should().Be("Rejected");
    }

    [Fact]
    public async Task DeleteAsync_WhenConfirmed_ThrowsSettlementNotPending()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);
        await harness.SettlementRecordService.ConfirmAsync(ownerId, settlement.Id, CancellationToken.None);

        var act = () => harness.SettlementRecordService.DeleteAsync(ownerId, settlement.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.SettlementNotPending);
    }

    [Fact]
    public async Task DeleteAsync_WhenPending_Succeeds()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        await harness.SettlementRecordService.DeleteAsync(ownerId, settlement.Id, CancellationToken.None);

        var remaining = await harness.SettlementRecordService.GetByGroupAsync(ownerId, group.Id, CancellationToken.None);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByGroupAsync_ListsAllStatuses()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        var settlements = await harness.SettlementRecordService.GetByGroupAsync(ownerId, group.Id, CancellationToken.None);

        settlements.Should().ContainSingle();
    }
}
