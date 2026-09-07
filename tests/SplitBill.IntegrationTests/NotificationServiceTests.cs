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

    // ⚠️ Bug thật phát hiện + sửa lúc làm tính năng "Quên mật khẩu" (2026-09-07, xem WebOptions):
    // LinkUrl gửi vào email trước đây luôn là đường dẫn TƯƠNG ĐỐI — không có nghĩa gì khi mở từ 1 email
    // client (không có "trang hiện tại" để tính tương đối theo), nên MỌI link "Xem chi tiết" trong MỌI
    // email thông báo trước đây đều là link hỏng. Test khóa lại: email phải chứa URL TUYỆT ĐỐI (có
    // scheme+host), trong khi Notification.LinkUrl lưu DB (dùng cho trang /Notifications same-origin)
    // vẫn giữ nguyên dạng tương đối.
    [Fact]
    public async Task NotifyAsync_WithLinkUrl_EmailBodyUsesAbsoluteUrl_ButStoredNotificationKeepsRelativeUrl()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var groupId = Guid.NewGuid();

        await harness.NotificationService.NotifyAsync(
            [new NotificationRecipient(userId, "a@example.com")],
            groupId, "ExpenseCreated", "Tieu de", "Noi dung", "/Expenses/Index/abc", CancellationToken.None);

        var sent = harness.EmailSender.SentEmails.Should().ContainSingle().Which;
        sent.HtmlBody.Should().Contain("http://localhost:5103/Expenses/Index/abc");

        var page = await harness.NotificationService.GetPagedAsync(userId, 1, 20, CancellationToken.None);
        page.Items[0].LinkUrl.Should().Be("/Expenses/Index/abc"); // DB vẫn tương đối, không đổi
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

    // ⚠️ Bảo mật (security-review 2026-09-07): message/title là dữ liệu người dùng tự đặt (DisplayName,
    // Expense.Title...) không giới hạn ký tự, trong khi email gửi dạng text/html — nếu không encode,
    // một thành viên ác ý có thể chèn thẻ <a>/<img> giả mạo vào email thông báo hợp lệ của app để
    // phishing thành viên khác. Test khẳng định BuildHtmlBody (private, chỉ verify được gián tiếp qua
    // HtmlBody đã gửi) luôn HtmlEncode message, và chỉ chấp nhận linkUrl dạng đường dẫn tương đối.
    [Fact]
    public async Task NotifyAsync_MessageContainsHtml_EmailBodyIsHtmlEncoded()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var groupId = Guid.NewGuid();
        const string malicious = "<a href=\"http://attacker.example/login\">Đăng nhập lại</a>";

        await harness.NotificationService.NotifyAsync(
            [new NotificationRecipient(userId, "a@example.com")],
            groupId, "ExpenseCreated", "Tieu de", malicious, "/link", CancellationToken.None);

        var sent = harness.EmailSender.SentEmails.Single();
        sent.HtmlBody.Should().NotContain("<a href=\"http://attacker.example/login\">");
        sent.HtmlBody.Should().Contain("&lt;a href=&quot;http://attacker.example/login&quot;&gt;");
    }

    [Fact]
    public async Task NotifyAsync_LinkUrlIsAbsolute_DroppedFromEmailBody()
    {
        using var harness = TestHarness.Create();
        var userId = await harness.RegisterUserAsync("a@example.com", "Nam");
        var groupId = Guid.NewGuid();

        await harness.NotificationService.NotifyAsync(
            [new NotificationRecipient(userId, "a@example.com")],
            groupId, "ExpenseCreated", "Tieu de", "Noi dung", "http://attacker.example/phish", CancellationToken.None);

        var sent = harness.EmailSender.SentEmails.Single();
        sent.HtmlBody.Should().NotContain("attacker.example");
        sent.HtmlBody.Should().NotContain("<a href");
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
