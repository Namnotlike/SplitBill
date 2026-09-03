using SplitBill.Domain.Enums;

namespace SplitBill.Domain.Entities;

/// <summary>
/// Một thành viên trong nhóm. Mọi tham chiếu tới "người" ở Expense/Settlement dùng GroupMemberId,
/// KHÔNG dùng UserId, để khách vãng lai (UserId == null) vẫn tham gia chia tiền được (CLAUDE.md mục 4.1).
/// </summary>
public sealed class GroupMember
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public GroupMemberRole Role { get; set; } = GroupMemberRole.Member;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; }

    public Group? Group { get; set; }
    public User? User { get; set; }
}
