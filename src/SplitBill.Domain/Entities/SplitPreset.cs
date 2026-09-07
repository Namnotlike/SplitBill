namespace SplitBill.Domain.Entities;

/// <summary>Mẫu cách chia hay dùng của 1 nhóm (CLAUDE.md mục 21, bổ sung 2026-09-07) — lưu lại
/// SplitMode + SplitConfig để chọn nhanh lúc tạo khoản chi mới (vd "Tôi & Bình chia đôi"), đỡ phải
/// tick lại từng người mỗi lần. KHÔNG lưu Payers/TotalAmount — chỉ phần "chia cho ai, theo tỉ lệ nào"
/// là cái lặp lại nhiều lần, còn "ai ứng tiền lần này" luôn khác nhau nên nhập mới mỗi lần.</summary>
public sealed class SplitPreset
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SplitMode { get; set; } = string.Empty;

    /// <summary>JSON của <c>SplitConfigInput</c> (cùng shape với <c>Expense.SplitConfigJson</c>,
    /// CLAUDE.md mục 4.1) — không có Payers.</summary>
    public string SplitConfigJson { get; set; } = string.Empty;

    public Guid CreatedByMemberId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
