using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.RecurringExpenses;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>CLAUDE.md mục 15.7 — Khoản chi định kỳ.</summary>
public sealed class RecurringExpenseTests
{
    private static async Task<(TestHarness Harness, Guid OwnerId, GroupDto Group, Guid OwnerMemberId, Guid GuestMemberId)> SetupRecurringGroupAsync()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Phong tro", null, "Recurring", "VND"), CancellationToken.None);
        var guest = await harness.GroupService.AddMemberAsync(ownerId, group.Id, new AddMemberRequest(null, "Binh"), CancellationToken.None);
        return (harness, ownerId, group, group.Members[0].Id, guest.Id);
    }

    [Fact]
    public async Task CreateAsync_OnOneTimeGroup_ThrowsGroupTypeNotRecurring()
    {
        var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var group = await harness.GroupService.CreateAsync(ownerId, new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);
        var ownerMemberId = group.Members[0].Id;

        var act = () => harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId]),
            "Monthly", DateTimeOffset.UtcNow), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.GroupTypeNotRecurring);
    }

    [Fact]
    public async Task CreateAsync_OnRecurringGroup_PersistsTemplate()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        var firstRunAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var template = await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Monthly", firstRunAt, Category: "Accommodation"), CancellationToken.None);

        template.Title.Should().Be("Tien nha");
        template.TotalAmount.Should().Be(3_000_000);
        template.Interval.Should().Be("Monthly");
        template.Category.Should().Be("Accommodation");
        template.NextRunAt.Should().Be(firstRunAt);
        template.IsActive.Should().BeTrue();
        template.Payers.Should().ContainSingle(p => p.MemberId == ownerMemberId && p.Amount == 3_000_000);
    }

    [Fact]
    public async Task CreateAsync_MemberNotInGroup_ThrowsMemberNotInGroup()
    {
        var (harness, ownerId, group, ownerMemberId, _) = await SetupRecurringGroupAsync();
        var strangerMemberId = Guid.NewGuid();

        var act = () => harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 1_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 1_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, strangerMemberId]),
            "Monthly", DateTimeOffset.UtcNow), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.MemberNotInGroup);
    }

    [Fact]
    public async Task GetByGroupIdAsync_ReturnsCreatedTemplates()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Monthly", DateTimeOffset.UtcNow), CancellationToken.None);

        var templates = await harness.RecurringExpenseService.GetByGroupIdAsync(ownerId, group.Id, CancellationToken.None);

        templates.Should().ContainSingle(t => t.Title == "Tien nha");
    }

    [Fact]
    public async Task DeactivateAsync_StopsFutureRuns()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        var now = DateTimeOffset.UtcNow;
        var template = await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Monthly", now.AddDays(-1)), CancellationToken.None);

        await harness.RecurringExpenseService.DeactivateAsync(ownerId, template.Id, CancellationToken.None);
        var createdCount = await harness.RecurringExpenseRunner.RunDueTemplatesAsync(now, CancellationToken.None);

        createdCount.Should().Be(0);
        var expenses = await harness.ExpenseService.GetPagedAsync(ownerId, group.Id, 1, 20, ExpenseFilter.Empty, CancellationToken.None);
        expenses.TotalCount.Should().Be(0);
    }

    // ===== RecurringExpenseRunner — sinh Expense từ mẫu tới hạn =====

    [Fact]
    public async Task RunDueTemplatesAsync_DueTemplate_CreatesExpenseAndAdvancesNextRunAt()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        var now = DateTimeOffset.UtcNow;
        var template = await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Monthly", now.AddMinutes(-1)), CancellationToken.None);

        var createdCount = await harness.RecurringExpenseRunner.RunDueTemplatesAsync(now, CancellationToken.None);

        createdCount.Should().Be(1);
        var expenses = await harness.ExpenseService.GetPagedAsync(ownerId, group.Id, 1, 20, ExpenseFilter.Empty, CancellationToken.None);
        expenses.Items.Should().ContainSingle(e => e.Title == "Tien nha" && e.TotalAmount == 3_000_000);
        expenses.Items[0].Splits.Sum(s => s.Amount).Should().Be(3_000_000);

        var templates = await harness.RecurringExpenseService.GetByGroupIdAsync(ownerId, group.Id, CancellationToken.None);
        // Monthly -> NextRunAt phải nhảy tới ~1 tháng sau lần chạy trước, chắc chắn > "now".
        templates.Single(t => t.Id == template.Id).NextRunAt.Should().BeAfter(now);
    }

    [Fact]
    public async Task RunDueTemplatesAsync_CalledTwiceForSameAsOf_OnlyCreatesOnce()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        var now = DateTimeOffset.UtcNow;
        await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Weekly", now.AddMinutes(-1)), CancellationToken.None);

        await harness.RecurringExpenseRunner.RunDueTemplatesAsync(now, CancellationToken.None);
        var secondRunCount = await harness.RecurringExpenseRunner.RunDueTemplatesAsync(now, CancellationToken.None);

        secondRunCount.Should().Be(0);
        var expenses = await harness.ExpenseService.GetPagedAsync(ownerId, group.Id, 1, 20, ExpenseFilter.Empty, CancellationToken.None);
        expenses.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task RunDueTemplatesAsync_LongOverdueDailyTemplate_CreatesOnlyOneExpense_NotOnePerMissedDay()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        var now = DateTimeOffset.UtcNow;
        // Mẫu Daily nhưng NextRunAt đã lỡ tới 10 ngày (giả lập server tắt lâu ngày) — không được bù
        // lại 10 khoản chi, chỉ sinh đúng 1 khoản cho lượt quét này (CLAUDE.md mục 15.7).
        await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien cho", 100_000, 0,
            [new ExpensePayerInput(ownerMemberId, 100_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Daily", now.AddDays(-10)), CancellationToken.None);

        var createdCount = await harness.RecurringExpenseRunner.RunDueTemplatesAsync(now, CancellationToken.None);

        createdCount.Should().Be(1);
        var expenses = await harness.ExpenseService.GetPagedAsync(ownerId, group.Id, 1, 20, ExpenseFilter.Empty, CancellationToken.None);
        expenses.TotalCount.Should().Be(1);

        var templates = await harness.RecurringExpenseService.GetByGroupIdAsync(ownerId, group.Id, CancellationToken.None);
        templates[0].NextRunAt.Should().BeAfter(now);
    }

    [Fact]
    public async Task RunDueTemplatesAsync_NotYetDue_DoesNotCreateExpense()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        var now = DateTimeOffset.UtcNow;
        await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Monthly", now.AddDays(5)), CancellationToken.None);

        var createdCount = await harness.RecurringExpenseRunner.RunDueTemplatesAsync(now, CancellationToken.None);

        createdCount.Should().Be(0);
    }

    // ⚠️ Bảo mật/nghiệp vụ (security-review 2026-09-07, quyết định người dùng "tắt mẫu + báo nhóm"):
    // GroupMemberId đóng băng trong mẫu lúc tạo có thể rời nhóm trước lần chạy kế tiếp (chỉ rời được
    // khi net == 0 lúc rời — ở đây guest chưa từng liên quan expense nào nên net sẵn = 0). Runner phải
    // phát hiện và tắt mẫu thay vì âm thầm sinh Expense gán tiền cho người đã rời nhóm (họ không còn
    // cách nào xem/tranh chấp vì /balances yêu cầu caller đang active).
    [Fact]
    public async Task RunDueTemplatesAsync_TemplateReferencesMemberWhoLeftGroup_DeactivatesTemplateInsteadOfCreatingExpense()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        var now = DateTimeOffset.UtcNow;
        var template = await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Monthly", now.AddMinutes(-1)), CancellationToken.None);
        await harness.GroupService.RemoveMemberAsync(ownerId, group.Id, guestMemberId, CancellationToken.None);

        var createdCount = await harness.RecurringExpenseRunner.RunDueTemplatesAsync(now, CancellationToken.None);

        createdCount.Should().Be(0);
        var expenses = await harness.ExpenseService.GetPagedAsync(ownerId, group.Id, 1, 20, ExpenseFilter.Empty, CancellationToken.None);
        expenses.Items.Should().BeEmpty();

        var templates = await harness.RecurringExpenseService.GetByGroupIdAsync(ownerId, group.Id, CancellationToken.None);
        templates.Single(t => t.Id == template.Id).IsActive.Should().BeFalse();

        // Owner (thành viên active còn lại, có tài khoản) phải được báo về việc mẫu bị tắt.
        var ownerNotifications = await harness.NotificationService.GetPagedAsync(ownerId, 1, 20, CancellationToken.None);
        ownerNotifications.Items.Should().Contain(n => n.Type == "RecurringTemplateDeactivated");
    }

    [Fact]
    public async Task RunDueTemplatesAsync_CreatedExpense_AppearsOnTimelineWithFullDetail()
    {
        var (harness, ownerId, group, ownerMemberId, guestMemberId) = await SetupRecurringGroupAsync();
        var now = DateTimeOffset.UtcNow;
        await harness.RecurringExpenseService.CreateAsync(ownerId, group.Id, new CreateRecurringExpenseRequest(
            "Tien nha", 3_000_000, 0,
            [new ExpensePayerInput(ownerMemberId, 3_000_000)],
            "Equal", new SplitConfigInput(MemberIds: [ownerMemberId, guestMemberId]),
            "Monthly", now.AddMinutes(-1)), CancellationToken.None);

        await harness.RecurringExpenseRunner.RunDueTemplatesAsync(now, CancellationToken.None);

        // Bug thật cần tránh: AfterJson rỗng ở audit log của khoản chi tự sinh sẽ làm Timeline (mục
        // 15.5) hiện dòng cụt lủn "đã thêm 1 khoản chi" thay vì có tên/số tiền cụ thể.
        var page = await harness.GroupService.GetAuditLogsAsync(ownerId, group.Id, 1, 20, CancellationToken.None);
        page.Items.Should().Contain(l => l.EntityType == "Expense" && l.Action == "Created" && l.Summary == "đã thêm khoản chi \"Tien nha\" (3.000.000đ)");
    }
}
