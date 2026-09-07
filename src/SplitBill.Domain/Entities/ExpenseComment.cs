namespace SplitBill.Domain.Entities;

/// <summary>Bình luận trên 1 khoản chi (CLAUDE.md mục 19, bổ sung 2026-09-07) — dùng để thắc mắc/giải
/// thích ngay dưới khoản chi (vd "sao khoản này tính cả phần của tao?"), không phải dữ liệu tài chính
/// nên không bắt buộc theo nguyên tắc bất di bất dịch mục 1 CLAUDE.md, nhưng vẫn soft-delete để nhất
/// quán với cách xử lý nội dung người dùng tạo ra ở phần còn lại của app.</summary>
public sealed class ExpenseComment
{
    public Guid Id { get; set; }
    public Guid ExpenseId { get; set; }

    /// <summary>GroupMemberId của người viết — LUÔN là chính người gọi API (đã đăng nhập), không cho
    /// viết hộ người khác. Dùng GroupMemberId (không phải UserId) để nhất quán với ExpensePayer/Split
    /// và để tên hiển thị (DisplayName) luôn khớp đúng snapshot trong nhóm đó.</summary>
    public Guid AuthorMemberId { get; set; }

    public string Content { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Expense? Expense { get; set; }
    public GroupMember? AuthorMember { get; set; }
}
