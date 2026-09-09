using SplitBill.Application.Expenses; // PagedResult<T>

namespace SplitBill.Application.Notifications;

public interface INotificationService
{
    /// <summary>
    /// Ghi thông báo in-app cho từng người nhận + gửi email (nếu có địa chỉ). Gọi SAU KHI thao tác
    /// nghiệp vụ chính đã SaveChanges thành công (CLAUDE.md mục 13.4) — không gộp transaction, lỗi
    /// gửi thông báo không được làm hỏng thao tác chính.
    /// </summary>
    Task NotifyAsync(
        IEnumerable<NotificationRecipient> recipients,
        Guid groupId,
        string type,
        string title,
        string message,
        string? linkUrl,
        CancellationToken cancellationToken);

    Task<PagedResult<NotificationDto>> GetPagedAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken);

    Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);

    Task MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken);

    // ===== Web Push (CLAUDE.md mục 25.7) =====

    /// <summary>VAPID public key để client subscribe — rỗng nếu Web Push chưa được cấu hình (tính
    /// năng tùy chọn, xem <see cref="Common.WebPushOptions"/>).</summary>
    string GetVapidPublicKey();

    /// <summary>Đăng ký/cập nhật 1 push subscription. Upsert theo Endpoint — cùng 1 trình duyệt có thể
    /// đăng ký lại dưới tài khoản khác (UserId được cập nhật sang caller mới nhất).</summary>
    Task SubscribeToPushAsync(Guid callerUserId, CreatePushSubscriptionRequest request, CancellationToken cancellationToken);

    /// <summary>Hủy đăng ký — idempotent, không báo lỗi nếu endpoint không tồn tại hoặc thuộc user
    /// khác (trình duyệt tự gọi khi tắt notification, không cần biết chi tiết).</summary>
    Task UnsubscribeFromPushAsync(Guid callerUserId, string endpoint, CancellationToken cancellationToken);
}
