using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class ExpenseCommentRepository : IExpenseCommentRepository
{
    private readonly SplitBillDbContext _dbContext;

    public ExpenseCommentRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ExpenseComment comment, CancellationToken cancellationToken) =>
        await _dbContext.ExpenseComments.AddAsync(comment, cancellationToken);

    public Task<List<ExpenseComment>> GetByExpenseIdAsync(Guid expenseId, CancellationToken cancellationToken) =>
        _dbContext.ExpenseComments
            .Include(c => c.AuthorMember)
            .Where(c => c.ExpenseId == expenseId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<ExpenseComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.ExpenseComments.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
}
