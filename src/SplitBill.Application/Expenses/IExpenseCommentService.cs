namespace SplitBill.Application.Expenses;

/// <summary>CLAUDE.md mục 19 — Bình luận trên khoản chi.</summary>
public interface IExpenseCommentService
{
    Task<IReadOnlyList<ExpenseCommentDto>> GetByExpenseIdAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken);

    Task<ExpenseCommentDto> CreateAsync(Guid callerUserId, Guid expenseId, CreateExpenseCommentRequest request, CancellationToken cancellationToken);

    /// <summary>Chỉ tác giả bình luận hoặc Owner của nhóm mới xóa được — ném 403 INSUFFICIENT_ROLE
    /// nếu không đủ quyền (khớp mẫu "đổi tên người khác chỉ Owner" ở mục 4.4).</summary>
    Task DeleteAsync(Guid callerUserId, Guid commentId, CancellationToken cancellationToken);
}
