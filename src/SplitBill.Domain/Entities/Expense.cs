using SplitBill.Domain.Enums;

namespace SplitBill.Domain.Entities;

/// <summary>Một khoản chi trong nhóm. Tách bạch "ai ứng tiền" (Payers) và "ai chịu tiền" (Splits).</summary>
public sealed class Expense
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Đơn vị đồng, phải &gt; 0.</summary>
    public long TotalAmount { get; set; }

    /// <summary>VAT/tip/phụ phí, &gt;= 0, phân bổ theo tỉ lệ phần gốc.</summary>
    public long ExtraFeeAmount { get; set; }

    public SplitMode SplitMode { get; set; }

    /// <summary>Lưu input gốc của người dùng (shares, %, itemized...) dạng JSON.</summary>
    public string? SplitConfigJson { get; set; }

    public string? Note { get; set; }
    public string? ReceiptImageUrl { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid CreatedByMemberId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token (CLAUDE.md mục 4.3/4.4).</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Group? Group { get; set; }
    public ICollection<ExpensePayer> Payers { get; set; } = new List<ExpensePayer>();
    public ICollection<ExpenseSplit> Splits { get; set; } = new List<ExpenseSplit>();
}
