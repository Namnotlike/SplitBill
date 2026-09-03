namespace SplitBill.Domain.Entities;

/// <summary>Nhật ký thay đổi cho mọi thao tác ghi lên dữ liệu tài chính (CLAUDE.md mục 4.1, mục 8).</summary>
public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }

    /// <summary>"Expense" | "Settlement" | "GroupMember" | "Group".</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>"Created" | "Updated" | "Deleted" | "Restored".</summary>
    public string Action { get; set; } = string.Empty;

    public Guid ActorMemberId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
