using SplitBill.Application.Expenses; // PagedResult<T>

namespace SplitBill.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    Guid GroupId,
    string Type,
    string Title,
    string Message,
    string? LinkUrl,
    bool IsRead,
    DateTimeOffset CreatedAt);

public sealed record UnreadCountDto(int Count);

/// <summary>1 người nhận thông báo — luôn là User có tài khoản (CLAUDE.md mục 13).</summary>
public sealed record NotificationRecipient(Guid UserId, string? Email);
