using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetPagedByUserIdAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken);

    Task<Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken);

    /// <summary>Đánh dấu mọi thông báo CHƯA đọc của user là đã đọc. Trả về số dòng đã cập nhật.</summary>
    Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken);
}
