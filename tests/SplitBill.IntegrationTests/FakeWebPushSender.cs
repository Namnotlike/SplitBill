using SplitBill.Application.Notifications;

namespace SplitBill.IntegrationTests;

/// <summary>Fake IWebPushSender ghi lại mọi push "đã gửi" để test assert — không dùng thư viện
/// mocking (không có trong danh sách NuGet được phép, CLAUDE.md mục 2). Cùng mẫu FakeEmailSender.
/// Đặt <see cref="ThrowGoneForEndpoint"/> để mô phỏng 1 subscription đã hết hạn (HTTP 404/410 thật từ
/// push service) — dùng để test luồng self-heal của NotificationService.</summary>
public sealed class FakeWebPushSender : IWebPushSender
{
    public List<(string Endpoint, string Title, string Body, string? Url)> SentPushes { get; } = new();

    public string? ThrowGoneForEndpoint { get; set; }

    public Task SendAsync(PushSubscriptionTarget subscription, string title, string body, string? url, CancellationToken cancellationToken)
    {
        if (subscription.Endpoint == ThrowGoneForEndpoint)
        {
            throw new PushSubscriptionGoneException($"Push subscription {subscription.Endpoint} không còn hợp lệ (giả lập test).");
        }

        SentPushes.Add((subscription.Endpoint, title, body, url));
        return Task.CompletedTask;
    }
}
