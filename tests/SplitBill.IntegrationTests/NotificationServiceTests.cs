using FluentAssertions;
using SplitBill.Application.Notifications;
using SplitBill.Domain.Exceptions;
using Xunit;

namespace SplitBill.IntegrationTests;

/// <summary>Test cho CLAUDE.md mục 13 — CRUD thông báo thuần (NotifyAsync/GetPaged/MarkAsRead...),
/// tách biệt với test xác nhận 4 điểm trigger (xem ExpenseServiceTests/SettlementRecordServiceTests/
/// GroupServiceTests).</summary>
public sealed class NotificationServiceTests
{
    [Fact]
    public async Task NotifyAsync_WithEmail_WritesInAppAndSendsEmail()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var groupId = Guid.NewGuid();

        await harness.NotificationService.NotifyAsync(
            [new NotificationRecipient(userId, "a@example.com")],
            groupId, "ExpenseCreated", "Tieu de", "Noi dung", "/link", CancellationToken.None);

        var page = await harness.NotificationService.GetPagedAsync(userId, 1, 20, CancellationToken.None);
        page.TotalCount.Should().Be(1);
        page.Items[0].Title.Should().Be("Tieu de");
        page.Items[0].IsRead.Should().BeFalse();

        harness.EmailSender.SentEmails.Should().ContainSingle(e => e.ToEmail == "a@example.com" && e.Subject == "Tieu de");
    }

    [Fact]
    public async Task NotifyAsync_RecipientWithNullEmail_WritesInAppOnly_NoEmailSent()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var groupId = Guid.NewGuid();

        await harness.NotificationService.NotifyAsync(
            [new NotificationRecipient(userId, null)],
            groupId, "ExpenseCreated", "Tieu de", "Noi dung", null, CancellationToken.None);

        var count = await harness.NotificationService.GetUnreadCountAsync(userId, CancellationToken.None);
        count.Should().Be(1);
        harness.EmailSender.SentEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkAsReadAsync_OwnNotification_MarksRead()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var groupId = Guid.NewGuid();
        await harness.NotificationService.NotifyAsync(
            [new NotificationRecipient(userId, null)], groupId, "ExpenseCreated", "T", "M", null, CancellationToken.None);
        var notificationId = (await harness.NotificationService.GetPagedAsync(userId, 1, 20, CancellationToken.None)).Items[0].Id;

        await harness.NotificationService.MarkAsReadAsync(userId, notificationId, CancellationToken.None);

        (await harness.NotificationService.GetUnreadCountAsync(userId, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task MarkAsReadAsync_OtherUsersNotification_ThrowsNotificationNotFound()
    {
        using var harness = TestHarness.Create();
        var ownerId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var strangerId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var groupId = Guid.NewGuid();
        await harness.NotificationService.NotifyAsync(
            [new NotificationRecipient(ownerId, null)], groupId, "ExpenseCreated", "T", "M", null, CancellationToken.None);
        var notificationId = (await harness.NotificationService.GetPagedAsync(ownerId, 1, 20, CancellationToken.None)).Items[0].Id;

        var act = () => harness.NotificationService.MarkAsReadAsync(strangerId, notificationId, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.ErrorCode.Should().Be(ErrorCodes.NotificationNotFound);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_MarksAllUnreadForUser_LeavesOtherUsersUntouched()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var otherId = await harness.RegisterUserAsync("b@example.com", "Binh");
        var groupId = Guid.NewGuid();
        await harness.NotificationService.NotifyAsync([new NotificationRecipient(userId, null)], groupId, "ExpenseCreated", "T1", "M", null, CancellationToken.None);
        await harness.NotificationService.NotifyAsync([new NotificationRecipient(userId, null)], groupId, "ExpenseCreated", "T2", "M", null, CancellationToken.None);
        await harness.NotificationService.NotifyAsync([new NotificationRecipient(otherId, null)], groupId, "ExpenseCreated", "T3", "M", null, CancellationToken.None);

        await harness.NotificationService.MarkAllAsReadAsync(userId, CancellationToken.None);

        (await harness.NotificationService.GetUnreadCountAsync(userId, CancellationToken.None)).Should().Be(0);
        (await harness.NotificationService.GetUnreadCountAsync(otherId, CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task GetPagedAsync_OrdersNewestFirst()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var groupId = Guid.NewGuid();
        await harness.NotificationService.NotifyAsync([new NotificationRecipient(userId, null)], groupId, "ExpenseCreated", "Dau tien", "M", null, CancellationToken.None);
        await harness.NotificationService.NotifyAsync([new NotificationRecipient(userId, null)], groupId, "ExpenseCreated", "Sau cung", "M", null, CancellationToken.None);

        var page = await harness.NotificationService.GetPagedAsync(userId, 1, 20, CancellationToken.None);

        page.Items[0].Title.Should().Be("Sau cung");
        page.Items[1].Title.Should().Be("Dau tien");
    }
}
