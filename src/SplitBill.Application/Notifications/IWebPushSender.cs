namespace SplitBill.Application.Notifications;

/// <summary>Đích gửi Web Push — không phải Domain entity, chỉ 3 field mà thư viện WebPush thật sự
/// cần. Application layer không phụ thuộc thư viện WebPush (chỉ Infrastructure mới tham chiếu NuGet
/// đó — implementation thật `WebPushSender` nằm ở SplitBill.Infrastructure.Push).</summary>
public sealed record PushSubscriptionTarget(string Endpoint, string P256dhKey, string AuthKey);

/// <summary>Ném khi push service (trình duyệt/OS) báo subscription không còn hợp lệ (HTTP 404/410 từ
/// WebPush) — NotificationService bắt exception này để tự xóa PushSubscription khỏi DB (self-heal).
/// Đây là vòng đời BÌNH THƯỜNG của Web Push (người dùng gỡ cài đặt app, xóa dữ liệu trình duyệt...),
/// không phải sự cố — khác các lỗi tạm thời khác (timeout, 5xx) chỉ log và giữ nguyên subscription để
/// thử lại ở thông báo kế tiếp.</summary>
public sealed class PushSubscriptionGoneException : Exception
{
    public PushSubscriptionGoneException(string message) : base(message)
    {
    }
}

/// <summary>CLAUDE.md mục 25.7. Interface thuần — implementation thật (thư viện WebPush, ký VAPID)
/// nằm ở SplitBill.Infrastructure, cùng mẫu IEmailSender.</summary>
public interface IWebPushSender
{
    Task SendAsync(PushSubscriptionTarget subscription, string title, string body, string? url, CancellationToken cancellationToken);
}
