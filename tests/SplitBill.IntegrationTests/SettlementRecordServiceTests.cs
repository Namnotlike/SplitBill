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

    // CLAUDE.md mục 25.2 — Miễn nợ.
    [Fact]
    public async Task WaiveAsync_ByCreditor_CreatesConfirmedWaivedSettlement()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        var settlement = await harness.SettlementRecordService.WaiveAsync(
            ownerId, group.Id, new WaiveSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        settlement.Status.Should().Be("Confirmed");
        settlement.IsWaived.Should().BeTrue();
        settlement.ConfirmedByMemberId.Should().Be(ownerMemberId);
        settlement.RecordedByMemberId.Should().Be(ownerMemberId);
    }

    [Fact]
    public async Task WaiveAsync_ByDebtorNotCreditor_ThrowsInsufficientRole()
    {
        var (harness, ownerId, guestUserId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        // guestUserId la nguoi NO (FromMemberId), khong phai chu no (ToMemberId=ownerMemberId) -> khong
        // duoc mien no thay chu no.
        var act = () => harness.SettlementRecordService.WaiveAsync(
            guestUserId, group.Id, new WaiveSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }

    [Fact]
    public async Task WaiveAsync_AppearsInGetByGroup_WithIsWaivedTrue()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        await harness.SettlementRecordService.WaiveAsync(
            ownerId, group.Id, new WaiveSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        var settlements = await harness.SettlementRecordService.GetByGroupAsync(ownerId, group.Id, CancellationToken.None);

        settlements.Should().ContainSingle(s => s.IsWaived && s.Status == "Confirmed");
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

    // CLAUDE.md mục 24 — khôi phục settlement đã xóa.
    [Fact]
    public async Task RestoreAsync_UndeletesSettlement_ReappearsInGetByGroup()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);
        await harness.SettlementRecordService.DeleteAsync(ownerId, settlement.Id, CancellationToken.None);

        var restored = await harness.SettlementRecordService.RestoreAsync(ownerId, settlement.Id, CancellationToken.None);

        restored.Status.Should().Be("Pending");
        var remaining = await harness.SettlementRecordService.GetByGroupAsync(ownerId, group.Id, CancellationToken.None);
        remaining.Should().ContainSingle(s => s.Id == settlement.Id);
    }

    [Fact]
    public async Task RestoreAsync_NotDeleted_ThrowsSettlementNotDeleted()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        var act = () => harness.SettlementRecordService.RestoreAsync(ownerId, settlement.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.SettlementNotDeleted);
    }

    [Fact]
    public async Task RestoreAsync_ByNeitherRecorderNorOwner_ThrowsInsufficientRole()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        // Owner ghi nhận -> chỉ Owner (người ghi nhận) hoặc 1 Owner khác được khôi phục. Guest không
        // phải người ghi nhận và không phải Owner -> phải bị chặn.
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);
        await harness.SettlementRecordService.DeleteAsync(ownerId, settlement.Id, CancellationToken.None);
        var guestUserId = harness.DbContext.Users.Single(u => u.Email == "b@example.com").Id;

        var act = () => harness.SettlementRecordService.RestoreAsync(guestUserId, settlement.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }

    [Fact]
    public async Task GetDeletedAsync_ReturnsOnlyDeletedSettlements()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var kept = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 10_000), CancellationToken.None);
        var deleted = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 20_000), CancellationToken.None);
        await harness.SettlementRecordService.DeleteAsync(ownerId, deleted.Id, CancellationToken.None);

        var deletedList = await harness.SettlementRecordService.GetDeletedAsync(ownerId, group.Id, CancellationToken.None);

        deletedList.Should().ContainSingle(s => s.Id == deleted.Id);
        deletedList.Should().NotContain(s => s.Id == kept.Id);
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

    // ===== Thông báo (CLAUDE.md mục 13) — bổ sung 2026-09-05 =====

    [Fact]
    public async Task CreateAsync_NotifiesReceiver()
    {
        var (harness, ownerId, _, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();

        // guest (From) ghi nhận đã chuyển cho owner (To) -> owner phải nhận thông báo.
        await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);

        var page = await harness.NotificationService.GetPagedAsync(ownerId, 1, 20, CancellationToken.None);
        page.TotalCount.Should().Be(1);
        page.Items[0].Type.Should().Be("SettlementRecorded");
        harness.EmailSender.SentEmails.Should().ContainSingle(e => e.ToEmail == "a@example.com");
    }

    [Fact]
    public async Task ConfirmAsync_NotifiesSender()
    {
        var (harness, ownerId, guestUserId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);
        harness.EmailSender.SentEmails.Clear();

        await harness.SettlementRecordService.ConfirmAsync(ownerId, settlement.Id, CancellationToken.None);

        var page = await harness.NotificationService.GetPagedAsync(guestUserId, 1, 20, CancellationToken.None);
        page.Items.Should().ContainSingle(n => n.Type == "SettlementConfirmed");
        harness.EmailSender.SentEmails.Should().ContainSingle(e => e.ToEmail == "b@example.com");
    }

    [Fact]
    public async Task RejectAsync_NotifiesSender()
    {
        var (harness, ownerId, guestUserId, group, ownerMemberId, guestMemberId) = await SetupGroupAsync();
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(guestMemberId, ownerMemberId, 50_000), CancellationToken.None);
        harness.EmailSender.SentEmails.Clear();

        await harness.SettlementRecordService.RejectAsync(ownerId, settlement.Id, CancellationToken.None);

        var page = await harness.NotificationService.GetPagedAsync(guestUserId, 1, 20, CancellationToken.None);
        page.Items.Should().ContainSingle(n => n.Type == "SettlementRejected");
    }

    [Fact]
    public async Task CreateAsync_ReceiverIsGuestWithoutAccount_NoNotificationOrEmail()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        // Khách vãng lai — không có UserId -> không có tài khoản để nhận thông báo.
        var guestMember = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Chi vang lai"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;

        // owner (From) ghi nhận đã chuyển cho khách vãng lai (To) -> không ai có tài khoản để báo.
        await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(ownerMemberId, guestMember.Id, 50_000), CancellationToken.None);

        (await harness.NotificationService.GetUnreadCountAsync(ownerId, CancellationToken.None)).Should().Be(0);
        harness.EmailSender.SentEmails.Should().BeEmpty();
    }
}
