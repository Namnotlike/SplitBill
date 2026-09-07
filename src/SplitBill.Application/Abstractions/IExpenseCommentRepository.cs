using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IExpenseCommentRepository
{
    Task AddAsync(ExpenseComment comment, CancellationToken cancellationToken);

    /// <summary>Kèm sẵn <see cref="ExpenseComment.AuthorMember"/> (Include) để lấy DisplayName mà
    /// không cần truy vấn thêm.</summary>
    Task<List<ExpenseComment>> GetByExpenseIdAsync(Guid expenseId, CancellationToken cancellationToken);

    Task<ExpenseComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
