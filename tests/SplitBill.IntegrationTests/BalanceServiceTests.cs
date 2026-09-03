using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Users;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

public sealed class BalanceServiceTests
{
    private static async Task<(TestHarness Harness, Guid OwnerId, GroupDto Group, Guid OwnerMemberId, Guid GuestMemberId)> SetupGroupWithExpenseAsync()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;

        // B1 (CLAUDE.md mục 7.2): A ứng 300k, chia đều A/B/C -> ở đây chỉ 2 người nên A:+150k, B:-150k
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 300_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 300_000)],
            "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guest.Id])), CancellationToken.None);

        return (harness, ownerId, group, ownerMemberId, guest.Id);
    }

    [Fact]
    public async Task GetBalancesAsync_ComputesCorrectNet()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupWithExpenseAsync();

        var balances = await harness.BalanceService.GetBalancesAsync(ownerId, group.Id, CancellationToken.None);

        balances.Single(b => b.MemberId == ownerMemberId).Net.Should().Be(150_000);
        balances.Single(b => b.MemberId == guestMemberId).Net.Should().Be(-150_000);
        balances.Sum(b => b.Net).Should().Be(0);
    }

    [Fact]
    public async Task GetSettlementPlanAsync_Simplified_ReturnsSingleTransaction()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupWithExpenseAsync();

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Simplified.Should().BeTrue();
        plan.Transactions.Should().ContainSingle();
        plan.Transactions[0].FromMemberId.Should().Be(guestMemberId);
        plan.Transactions[0].ToMemberId.Should().Be(ownerMemberId);
        plan.Transactions[0].Amount.Should().Be(150_000);
    }

    [Fact]
    public async Task GetSettlementPlanAsync_NoBankInfo_VietQrIsNull()
    {
        var (harness, ownerId, group, _, _) = await SetupGroupWithExpenseAsync();

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Transactions[0].VietQr.Should().BeNull();
    }

    [Fact]
    public async Task GetSettlementPlanAsync_WithBankInfo_VietQrIsPresent()
    {
        var (harness, ownerId, group, _, _) = await SetupGroupWithExpenseAsync();
        await harness.UserService.UpdateProfileAsync(ownerId, new UpdateProfileRequest("Nam", "0011001234567", "970436"), CancellationToken.None);

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Transactions[0].VietQr.Should().NotBeNull();
        plan.Transactions[0].VietQr!.Payload.Should().StartWith("000201");
    }

    [Fact]
    public async Task GetSettlementPlanAsync_NonSimplified_ReturnsDirectTransaction()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupGroupWithExpenseAsync();
        await harness.GroupService.UpdateAsync(ownerId, group.Id, new UpdateGroupRequest(null, null, false, null), CancellationToken.None);

        var plan = await harness.BalanceService.GetSettlementPlanAsync(ownerId, group.Id, CancellationToken.None);

        plan.Simplified.Should().BeFalse();
        plan.Transactions.Should().ContainSingle(t => t.FromMemberId == guestMemberId && t.ToMemberId == ownerMemberId && t.Amount == 150_000);
    }

    [Fact]
    public async Task GetBalancesAsync_NonMember_ThrowsMemberNotInGroup()
    {
        var (harness, _, group, _, _) = await SetupGroupWithExpenseAsync();
        var strangerId = await harness.RegisterUserAsync("c@example.com", "La");

        var act = () => harness.BalanceService.GetBalancesAsync(strangerId, group.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }
}
