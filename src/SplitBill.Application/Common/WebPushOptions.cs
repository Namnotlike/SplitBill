namespace SplitBill.Application.Common;

/// <summary>Cấu hình VAPID cho Web Push (CLAUDE.md mục 25.7). Không fail-fast nếu thiếu — cùng
/// nguyên tắc `GoogleAuthOptions`/`SmtpOptions`: đây là tính năng tùy chọn, để trống chỉ khiến tính
/// năng Web Push không hoạt động (client không lấy được VAPID public key nên không subscribe được),
/// không chặn Api chạy.</summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    public string VapidPublicKey { get; set; } = string.Empty;
    public string VapidPrivateKey { get; set; } = string.Empty;

    /// <summary>Bắt buộc theo chuẩn VAPID (RFC 8292) — 1 địa chỉ liên hệ dạng "mailto:..." hoặc URL
    /// "https://..." để push service (FCM/Mozilla...) biết ai đứng sau nếu cần liên hệ.</summary>
    public string VapidSubject { get; set; } = "mailto:admin@example.com";
}
