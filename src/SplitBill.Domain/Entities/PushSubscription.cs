namespace SplitBill.Domain.Entities;

/// <summary>Đăng ký nhận thông báo đẩy trình duyệt (Web Push, CLAUDE.md mục 25.7, bổ sung 2026-09-09)
/// — 1 dòng cho mỗi cặp (trình duyệt, thiết bị) đã bấm "Bật thông báo đẩy". <see cref="Endpoint"/> là
/// URL do chính push service của trình duyệt (Chrome/Firefox/Edge...) cấp lúc subscribe, duy nhất cho
/// mỗi lượt đăng ký — KHÔNG duy nhất theo User, vì cùng 1 trình duyệt/thiết bị có thể đăng nhập lần
/// lượt bởi nhiều tài khoản khác nhau (đăng ký lại thì cập nhật UserId sang tài khoản mới nhất, xem
/// <c>NotificationService.SubscribeAsync</c>).</summary>
public sealed class PushSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dhKey { get; set; } = string.Empty;
    public string AuthKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
