using SplitBill.Domain.Enums;

namespace SplitBill.Domain.Entities;

/// <summary>
/// Mẫu khoản chi định kỳ (CLAUDE.md mục 15.7, bổ sung 2026-09-05) — chỉ áp dụng cho nhóm
/// <see cref="GroupType.Recurring"/>. <see cref="RecurringExpenseRunner"/> quét các mẫu có
/// <see cref="NextRunAt"/> đã tới hạn và tự sinh 1 <see cref="Expense"/> mới, giữ nguyên
/// Title/TotalAmount/ExtraFeeAmount/SplitMode/SplitConfigJson/Payers/Category/Note cho mỗi lần sinh.
/// </summary>
public sealed class RecurringExpenseTemplate
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public long TotalAmount { get; set; }
    public long ExtraFeeAmount { get; set; }
    public SplitMode SplitMode { get; set; }
    public string? SplitConfigJson { get; set; }

    /// <summary>JSON của <c>IReadOnlyList&lt;ExpensePayerInput&gt;</c> — ai ứng tiền mỗi lần khoản
    /// chi được tự sinh (thường là 1 người cố định, ví dụ chủ nhà trọ, nhưng không giới hạn).</summary>
    public string PayersJson { get; set; } = string.Empty;

    public ExpenseCategory Category { get; set; }
    public string? Note { get; set; }
    public RecurrenceInterval Interval { get; set; }

    /// <summary>Lần sinh khoản chi kế tiếp — đã tới hạn khi &lt;= thời điểm quét hiện tại.</summary>
    public DateTimeOffset NextRunAt { get; set; }

    /// <summary>Tắt mà không xóa (giữ lịch sử các khoản chi đã sinh ra từ mẫu này).</summary>
    public bool IsActive { get; set; } = true;

    public Guid CreatedByMemberId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Group? Group { get; set; }
}
