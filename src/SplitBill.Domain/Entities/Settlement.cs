using SplitBill.Domain.Enums;

namespace SplitBill.Domain.Entities;

/// <summary>Một lượt thanh toán thực tế đã diễn ra giữa 2 thành viên.</summary>
public sealed class Settlement
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid FromMemberId { get; set; }
    public Guid ToMemberId { get; set; }

    /// <summary>Phải &gt; 0.</summary>
    public long Amount { get; set; }

    public SettlementStatus Status { get; set; } = SettlementStatus.Pending;
    public Guid RecordedByMemberId { get; set; }
    public Guid? ConfirmedByMemberId { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? Note { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Lần gần nhất hệ thống đã nhắc người nhận (ToMember) xác nhận settlement này khi còn
    /// Pending quá lâu (CLAUDE.md mục 15.8, bổ sung 2026-09-05). Null nghĩa là chưa từng nhắc.</summary>
    public DateTimeOffset? LastReminderSentAt { get; set; }

    /// <summary>Optimistic concurrency token (CLAUDE.md mục 4.3/4.4).</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Group? Group { get; set; }
}
