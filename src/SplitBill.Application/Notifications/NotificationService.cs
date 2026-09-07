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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<NotificationService> _logger;
    private readonly WebOptions _webOptions;

    public NotificationService(
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork,
        IEmailSender emailSender,
        ILogger<NotificationService> logger,
        IOptions<WebOptions> webOptions)
    {
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
        _emailSender = emailSender;
        _logger = logger;
        _webOptions = webOptions.Value;
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
