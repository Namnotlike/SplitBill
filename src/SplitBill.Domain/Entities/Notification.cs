namespace SplitBill.Domain.Entities;

/// <summary>
/// Thông báo trong app cho 1 user (CLAUDE.md mục 13). LUÔN gắn với <see cref="User"/> có tài khoản
/// (UserId), không dùng GroupMemberId — khách vãng lai (GroupMember.UserId == null) không có tài
/// khoản để đăng nhập xem, nên không bao giờ là người nhận thông báo.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid GroupId { get; set; }

    /// <summary>"ExpenseCreated" | "SettlementRecorded" | "SettlementConfirmed" | "SettlementRejected" | "MemberAdded".</summary>
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Đường dẫn tương đối trên Web để điều hướng khi bấm vào thông báo, vd "/Expenses/Index/{groupId}".</summary>
    public string? LinkUrl { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
