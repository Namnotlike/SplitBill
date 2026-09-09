using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Notifications;

/// <summary>Cài đặt CLAUDE.md mục 13. Ghi thông báo in-app + gửi email, độc lập với transaction của
/// thao tác nghiệp vụ chính đã gọi trước đó.</summary>
public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IPushSubscriptionRepository _pushSubscriptionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly IWebPushSender _webPushSender;
    private readonly ILogger<NotificationService> _logger;
    private readonly WebOptions _webOptions;
    private readonly WebPushOptions _webPushOptions;

    public NotificationService(
        INotificationRepository notificationRepository,
        IPushSubscriptionRepository pushSubscriptionRepository,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        IWebPushSender webPushSender,
        ILogger<NotificationService> logger,
        IOptions<WebOptions> webOptions,
        IOptions<WebPushOptions> webPushOptions)
    {
        _notificationRepository = notificationRepository;
        _pushSubscriptionRepository = pushSubscriptionRepository;
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _webPushSender = webPushSender;
        _logger = logger;
        _webOptions = webOptions.Value;
        _webPushOptions = webPushOptions.Value;
    }

    public async Task NotifyAsync(
        IEnumerable<NotificationRecipient> recipients,
        Guid groupId,
        string type,
        string title,
        string message,
        string? linkUrl,
        CancellationToken cancellationToken)
    {
        var notifications = recipients.Select(r => new Notification
        {
            Id = Guid.NewGuid(),
            UserId = r.UserId,
            GroupId = groupId,
            Type = type,
            Title = title,
            Message = message,
            LinkUrl = linkUrl,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
        }).ToList();

        if (notifications.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var notification in notifications)
            {
                await _notificationRepository.AddAsync(notification, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Không được để lỗi ghi thông báo làm hỏng thao tác nghiệp vụ chính đã thành công trước
            // đó (mục 13.3) — chỉ log, không throw ra ngoài.
            _logger.LogError(ex, "Không ghi được thông báo in-app cho nhóm {GroupId}, loại {Type}", groupId, type);
            return;
        }

        var emailRecipients = recipients.Where(r => !string.IsNullOrWhiteSpace(r.Email)).ToList();
        foreach (var recipient in emailRecipients)
        {
            try
            {
                await _emailSender.SendAsync(recipient.Email!, title, BuildHtmlBody(title, message, linkUrl), cancellationToken);
            }
            catch (Exception ex)
            {
                // Lỗi SMTP không được chặn các người nhận còn lại, cũng không được throw ra ngoài.
                _logger.LogError(ex, "Không gửi được email thông báo tới {Email}", recipient.Email);
            }
        }

        await SendWebPushAsync(recipients, title, message, linkUrl, cancellationToken);
    }

    private async Task SendWebPushAsync(
        IEnumerable<NotificationRecipient> recipients, string title, string message, string? linkUrl, CancellationToken cancellationToken)
    {
        // Tính năng tùy chọn (mục 25.7) — chưa cấu hình VAPID thì bỏ qua ngay, không tốn 1 lượt đọc
        // PushSubscription nào cho mỗi người nhận.
        if (string.IsNullOrWhiteSpace(_webPushOptions.VapidPublicKey) || string.IsNullOrWhiteSpace(_webPushOptions.VapidPrivateKey))
        {
            return;
        }

        var absoluteUrl = linkUrl is not null && linkUrl.StartsWith('/') && !linkUrl.StartsWith("//", StringComparison.Ordinal)
            ? _webOptions.BaseUrl.TrimEnd('/') + linkUrl
            : null;

        foreach (var recipient in recipients)
        {
            List<PushSubscription> subscriptions;
            try
            {
                subscriptions = await _pushSubscriptionRepository.GetByUserIdAsync(recipient.UserId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không đọc được push subscription cho user {UserId}", recipient.UserId);
                continue;
            }

            foreach (var subscription in subscriptions)
            {
                try
                {
                    await _webPushSender.SendAsync(
                        new PushSubscriptionTarget(subscription.Endpoint, subscription.P256dhKey, subscription.AuthKey),
                        title, message, absoluteUrl, cancellationToken);
                }
                catch (PushSubscriptionGoneException)
                {
                    // Self-heal: subscription hết hạn/bị thu hồi phía trình duyệt/OS là vòng đời BÌNH
                    // THƯỜNG của Web Push (mục 25.7), không phải sự cố — xóa khỏi DB, không log lỗi.
                    try
                    {
                        await _pushSubscriptionRepository.DeleteAsync(subscription, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogError(cleanupEx, "Không xóa được push subscription hết hạn {Endpoint}", subscription.Endpoint);
                    }
                }
                catch (Exception ex)
                {
                    // Lỗi tạm thời (timeout, 5xx từ push service...) — không xóa subscription, chỉ log,
                    // không được chặn người nhận/subscription còn lại.
                    _logger.LogError(ex, "Không gửi được push notification tới subscription {Endpoint}", subscription.Endpoint);
                }
            }
        }
    }

    public string GetVapidPublicKey() => _webPushOptions.VapidPublicKey;

    public async Task SubscribeToPushAsync(Guid callerUserId, CreatePushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var existing = await _pushSubscriptionRepository.GetByEndpointAsync(request.Endpoint, cancellationToken);
        if (existing is not null)
        {
            // Cùng 1 trình duyệt/thiết bị có thể đã đăng ký trước đó dưới tài khoản khác — lần đăng ký
            // mới nhất "thắng" (cập nhật UserId + keys), không tạo dòng trùng.
            existing.UserId = callerUserId;
            existing.P256dhKey = request.P256dhKey;
            existing.AuthKey = request.AuthKey;
        }
        else
        {
            await _pushSubscriptionRepository.AddAsync(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = callerUserId,
                Endpoint = request.Endpoint,
                P256dhKey = request.P256dhKey,
                AuthKey = request.AuthKey,
                CreatedAt = DateTimeOffset.UtcNow,
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task UnsubscribeFromPushAsync(Guid callerUserId, string endpoint, CancellationToken cancellationToken)
    {
        var existing = await _pushSubscriptionRepository.GetByEndpointAsync(endpoint, cancellationToken);
        if (existing is not null && existing.UserId == callerUserId)
        {
            await _pushSubscriptionRepository.DeleteAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        // Không tồn tại hoặc thuộc user khác -> coi như đã "hủy đăng ký" thành công (idempotent),
        // không báo lỗi — xem doc comment INotificationService.UnsubscribeFromPushAsync.
    }

    public async Task<PagedResult<NotificationDto>> GetPagedAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (items, total) = await _notificationRepository.GetPagedByUserIdAsync(userId, page, pageSize, cancellationToken);
        return new PagedResult<NotificationDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        _notificationRepository.GetUnreadCountAsync(userId, cancellationToken);

    public async Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotificationNotFound, "Không tìm thấy thông báo.");

        if (notification.UserId != userId)
        {
            // Cố tình trả về NOT_FOUND thay vì FORBIDDEN — không tiết lộ thông báo này có tồn tại
            // hay không cho người không phải chủ sở hữu.
            throw new DomainException(ErrorCodes.NotificationNotFound, "Không tìm thấy thông báo.");
        }

        notification.IsRead = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _notificationRepository.MarkAllAsReadAsync(userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    // ⚠️ Bảo mật (phát hiện qua security-review 2026-09-07): `message`/`title` chứa dữ liệu
    // người dùng tự đặt (DisplayName, Expense.Title...) không giới hạn ký tự. Email được gửi dạng
    // text/html (SmtpEmailSender), nên PHẢI HtmlEncode trước khi ghép chuỗi — nếu không, một thành
    // viên ác ý có thể chèn thẻ <a>/<img> giả mạo vào email thông báo gửi từ địa chỉ hợp lệ của app,
    // dùng để phishing các thành viên khác (họ vốn tin tưởng email này). `linkUrl` luôn do chính
    // service nội bộ tự dựng (dạng "/Expenses/Index/{groupId}"), không phải input người dùng, nhưng
    // vẫn ràng buộc phải là đường dẫn tương đối bắt đầu bằng "/" làm phòng vệ theo chiều sâu — không
    // bao giờ tin tưởng render thẳng một URL tuyệt đối vào href của email.
    private string BuildHtmlBody(string title, string message, string? linkUrl)
    {
        var safeMessage = WebUtility.HtmlEncode(message);
        // Ghép thành URL TUYỆT ĐỐI ở đây — xem ghi chú đầy đủ trong WebOptions về lý do (link tương
        // đối không mở được từ email client). Notification.LinkUrl lưu trong DB (dùng cho trang
        // /Notifications same-origin) không đổi, chỉ email mới cần ghép base URL.
        var safeLinkUrl = linkUrl is not null && linkUrl.StartsWith('/') && !linkUrl.StartsWith("//", StringComparison.Ordinal)
            ? WebUtility.HtmlEncode(_webOptions.BaseUrl.TrimEnd('/') + linkUrl)
            : null;

        return safeLinkUrl is null
            ? $"<p>{safeMessage}</p>"
            : $"<p>{safeMessage}</p><p><a href=\"{safeLinkUrl}\">Xem chi tiết</a></p>";
    }

    private static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.GroupId, n.Type, n.Title, n.Message, n.LinkUrl, n.IsRead, n.CreatedAt);
}
