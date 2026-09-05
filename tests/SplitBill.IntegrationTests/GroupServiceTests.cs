using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

public sealed class GroupServiceTests
{
    [Fact]
    public async Task CreateAsync_AssignsOwnerRole_WithCreatorsRealDisplayName()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");

        var group = await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        group.Members.Should().ContainSingle();
        group.Members[0].Role.Should().Be("Owner");
        // Bug đã sửa ở M4: DisplayName phải là tên user thật, KHÔNG phải tên nhóm.
        group.Members[0].DisplayName.Should().Be("Nam");
    }

    // ===== Đa tiền tệ (CLAUDE.md mục 14) — bổ sung 2026-09-05 =====

    [Fact]
    public async Task CreateAsync_WithUsdCurrency_PersistsCurrency()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");

        var group = await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("Du lich My", null, "OneTime", "USD"), CancellationToken.None);

        group.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task CreateAsync_EmptyCurrency_DefaultsToVnd()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");

        var group = await harness.GroupService.CreateAsync(userId, new CreateGroupRequest("Du lich", null, "OneTime", null), CancellationToken.None);

        group.Currency.Should().Be("VND");
    }

    [Fact]
    public async Task AddMember_Guest_DefaultsToMemberRole()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        var member = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);

        member.Role.Should().Be("Member");
        member.UserId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ByNonOwner_ThrowsInsufficientRole()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var memberUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberUserId, "Binh"), CancellationToken.None);

        var act = () => harness.GroupService.UpdateAsync(memberUserId, group.Id, new UpdateGroupRequest("Ten moi", null, null, null), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }

    [Fact]
    public async Task UpdateMemberAsync_SelfRename_AllowedForAnyRole()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var memberUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var member = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberUserId, "Binh"), CancellationToken.None);

        var updated = await harness.GroupService.UpdateMemberAsync(memberUserId, group.Id, member.Id, new UpdateMemberRequest("Binh moi"), CancellationToken.None);

        updated.DisplayName.Should().Be("Binh moi");
    }

    [Fact]
    public async Task UpdateMemberAsync_RenameOtherByNonOwner_ThrowsInsufficientRole()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var memberUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var member = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberUserId, "Binh"), CancellationToken.None);

        // Binh (Member) cố đổi tên của Owner (Nam) -> phải bị chặn.
        var ownerMemberId = group.Members[0].Id;
        var act = () => harness.GroupService.UpdateMemberAsync(memberUserId, group.Id, ownerMemberId, new UpdateMemberRequest("Hacked"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }

    [Fact]
    public async Task RemoveMemberAsync_WithOutstandingBalance_ThrowsMemberHasOutstandingBalance()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;

        var expenseBody = new Application.Expenses.CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new Application.Expenses.ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal",
            new Application.Expenses.SplitConfigInput(MemberIds: [ownerMemberId, guest.Id]));
        await harness.ExpenseService.CreateAsync(ownerId, group.Id, expenseBody, CancellationToken.None);

        var act = () => harness.GroupService.RemoveMemberAsync(ownerId, group.Id, guest.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberHasOutstandingBalance);
    }

    [Fact]
    public async Task RemoveMemberAsync_ZeroBalance_Succeeds()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);

        await harness.GroupService.RemoveMemberAsync(ownerId, group.Id, guest.Id, CancellationToken.None);

        var refreshed = await harness.GroupService.GetByIdAsync(ownerId, group.Id, CancellationToken.None);
        refreshed.Members.Should().ContainSingle(m => m.Id == group.Members[0].Id);
    }

    [Fact]
    public async Task RemoveMemberAsync_LastOwner_ThrowsLastOwnerCannotBeRemoved()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;

        var act = () => harness.GroupService.RemoveMemberAsync(ownerId, group.Id, ownerMemberId, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.LastOwnerCannotBeRemoved);
    }

    [Fact]
    public async Task GetBySharedTokenAsync_NoAuth_ReturnsGroup()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        var shared = await harness.GroupService.GetBySharedTokenAsync(group.ShareToken, CancellationToken.None);

        shared.Id.Should().Be(group.Id);
    }

    [Fact]
    public async Task RotateShareTokenAsync_ByNonOwner_ThrowsInsufficientRole()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var memberUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberUserId, "Binh"), CancellationToken.None);

        var act = () => harness.GroupService.RotateShareTokenAsync(memberUserId, group.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }

    [Fact]
    public async Task RotateShareTokenAsync_ByOwner_ChangesToken()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        var newToken = await harness.GroupService.RotateShareTokenAsync(ownerId, group.Id, CancellationToken.None);

        newToken.Should().NotBe(group.ShareToken);
    }

    [Fact]
    public async Task GetByIdAsync_NonMember_ThrowsMemberNotInGroup()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var strangerId = await harness.RegisterUserAsync("c@example.com", "Nguoi la");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        var act = () => harness.GroupService.GetByIdAsync(strangerId, group.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }

    // ===== UpdateMemberRoleAsync — bổ sung khi rà soát 2026-09-04, endpoint chuyển nhượng Owner
    // trước đó có trong CLAUDE.md mục 4.4 nhưng chưa từng được cài đặt =====

    [Fact]
    public async Task UpdateMemberRoleAsync_ByOwner_PromotesMemberToOwner()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var memberUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var member = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberUserId, "Binh"), CancellationToken.None);

        var updated = await harness.GroupService.UpdateMemberRoleAsync(ownerId, group.Id, member.Id, new UpdateMemberRoleRequest("Owner"), CancellationToken.None);

        updated.Role.Should().Be("Owner");
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_ByNonOwner_ThrowsInsufficientRole()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var memberUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var member = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberUserId, "Binh"), CancellationToken.None);

        var act = () => harness.GroupService.UpdateMemberRoleAsync(memberUserId, group.Id, member.Id, new UpdateMemberRoleRequest("Owner"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.InsufficientRole);
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_DemoteLastOwner_ThrowsLastOwnerCannotBeRemoved()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;

        var act = () => harness.GroupService.UpdateMemberRoleAsync(ownerId, group.Id, ownerMemberId, new UpdateMemberRoleRequest("Member"), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.LastOwnerCannotBeRemoved);
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_DemoteOwner_WithOtherOwnerPresent_Succeeds()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var memberUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var member = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(memberUserId, "Binh"), CancellationToken.None);
        await harness.GroupService.UpdateMemberRoleAsync(ownerId, group.Id, member.Id, new UpdateMemberRoleRequest("Owner"), CancellationToken.None);
        var originalOwnerMemberId = group.Members[0].Id;

        var demoted = await harness.GroupService.UpdateMemberRoleAsync(memberUserId, group.Id, originalOwnerMemberId, new UpdateMemberRoleRequest("Member"), CancellationToken.None);

        demoted.Role.Should().Be("Member");
    }

    // ===== GetAuditLogsAsync — bổ sung khi rà soát 2026-09-04, GET /groups/{id}/audit-logs trước đó
    // có trong CLAUDE.md mục 8 nhưng chưa từng được cài đặt (chỉ ghi được, không đọc lại được) =====

    [Fact]
    public async Task GetAuditLogsAsync_AfterCreatingGroupAndMember_ReturnsEntriesNewestFirst()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);

        var page = await harness.GroupService.GetAuditLogsAsync(ownerId, group.Id, 1, 20, CancellationToken.None);

        page.TotalCount.Should().Be(2); // "Created" nhóm + "Created" thành viên
        page.Items[0].Action.Should().Be("Created");
        page.Items[0].EntityType.Should().Be("GroupMember"); // mới nhất trước
        page.Items[0].ActorMemberName.Should().Be("Nam");
        page.Items[0].Summary.Should().Be("đã thêm Binh vào nhóm");
    }

    // ===== Timeline hoạt động nhóm (CLAUDE.md mục 15.5) — Summary dựng từ Before/AfterJson, bổ
    // sung 2026-09-05 =====

    [Fact]
    public async Task GetAuditLogsAsync_ExpenseCreatedAndDeleted_SummaryIncludesTitleAndAmount()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        var expense = await harness.ExpenseService.CreateAsync(ownerId, group.Id, new CreateExpenseRequest(
            "An toi", 100_000, 0, DateTimeOffset.UtcNow,
            [new ExpensePayerInput(ownerMemberId, 100_000)], "Equal",
            new SplitConfigInput(MemberIds: [ownerMemberId, guest.Id])), CancellationToken.None);
        await harness.ExpenseService.DeleteAsync(ownerId, expense.Data.Id, CancellationToken.None);

        var page = await harness.GroupService.GetAuditLogsAsync(ownerId, group.Id, 1, 20, CancellationToken.None);

        page.Items.Should().Contain(l => l.EntityType == "Expense" && l.Action == "Created" && l.Summary == "đã thêm khoản chi \"An toi\" (100.000đ)");
        // Bug thật đã sửa (CLAUDE.md mục 15.5): trước đây Delete ghi BeforeJson=null nên không thể
        // biết tên/số tiền khoản chi đã xóa — giờ phải hiện đủ trong Summary.
        page.Items.Should().Contain(l => l.EntityType == "Expense" && l.Action == "Deleted" && l.Summary == "đã xóa khoản chi \"An toi\" (100.000đ)");
    }

    [Fact]
    public async Task GetAuditLogsAsync_SettlementConfirmed_SummaryDescribesConfirmation()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var nam = group.Members[0];
        var binh = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        var settlement = await harness.SettlementRecordService.CreateAsync(
            ownerId, group.Id, new CreateSettlementRequest(binh.Id, nam.Id, 50_000, null), CancellationToken.None);
        await harness.SettlementRecordService.ConfirmAsync(ownerId, settlement.Id, CancellationToken.None);

        var page = await harness.GroupService.GetAuditLogsAsync(ownerId, group.Id, 1, 20, CancellationToken.None);

        page.Items.Should().Contain(l => l.EntityType == "Settlement" && l.Summary == "đã xác nhận nhận 50.000đ từ Binh");
    }

    [Fact]
    public async Task GetAuditLogsAsync_MemberRemoved_SummaryNamesTheRemovedMember()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        await harness.GroupService.RemoveMemberAsync(ownerId, group.Id, guest.Id, CancellationToken.None);

        var page = await harness.GroupService.GetAuditLogsAsync(ownerId, group.Id, 1, 20, CancellationToken.None);

        // Bug thật đã sửa (CLAUDE.md mục 15.5): trước đây Delete ghi BeforeJson=null nên Summary
        // không thể nêu tên NGƯỜI BỊ XÓA (khác ActorMemberName vốn chỉ cho biết ai thực hiện hành động).
        page.Items.Should().Contain(l => l.EntityType == "GroupMember" && l.Action == "Deleted" && l.Summary == "đã xóa Binh khỏi nhóm");
    }

    [Fact]
    public async Task GetAuditLogsAsync_ByNonMember_ThrowsMemberNotInGroup()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var strangerId = await harness.RegisterUserAsync("c@example.com", "Nguoi la");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        var act = () => harness.GroupService.GetAuditLogsAsync(strangerId, group.Id, 1, 20, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }

    // ===== Thông báo "được thêm vào nhóm mới" (CLAUDE.md mục 13) — bổ sung 2026-09-05 =====

    [Fact]
    public async Task AddMemberAsync_WithUserId_NotifiesAddedUser()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var newUserId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(newUserId, "Binh"), CancellationToken.None);

        var page = await harness.NotificationService.GetPagedAsync(newUserId, 1, 20, CancellationToken.None);
        page.Items.Should().ContainSingle(n => n.Type == "MemberAdded");
        harness.EmailSender.SentEmails.Should().ContainSingle(e => e.ToEmail == "b@example.com");
    }

    [Fact]
    public async Task AddMemberAsync_GuestWithoutUserId_NoNotification()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Khach vang lai"), CancellationToken.None);

        harness.EmailSender.SentEmails.Should().BeEmpty();
    }
}
