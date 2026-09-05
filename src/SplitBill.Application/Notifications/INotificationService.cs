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
}
