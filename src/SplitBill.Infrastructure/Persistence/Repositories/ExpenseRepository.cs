using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class ExpenseRepository : IExpenseRepository
{
    private readonly SplitBillDbContext _dbContext;

    public ExpenseRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Expense?> GetByIdAsync(Guid expenseId, CancellationToken cancellationToken) =>
        _dbContext.Expenses
            .Include(e => e.Payers)
            .Include(e => e.Splits)
            .FirstOrDefaultAsync(e => e.Id == expenseId, cancellationToken);

    public async Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedAsync(
        Guid groupId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.Expenses
            .Where(e => e.GroupId == groupId)
            .OrderByDescending(e => e.OccurredAt);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(e => e.Payers)
            .Include(e => e.Splits)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<List<Expense>> GetAllByGroupIdAsync(Guid groupId, CancellationToken cancellationToken) =>
        _dbContext.Expenses
            .Where(e => e.GroupId == groupId)
            .Include(e => e.Payers)
            .Include(e => e.Splits)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Expense expense, CancellationToken cancellationToken) =>
        await _dbContext.Expenses.AddAsync(expense, cancellationToken);

    public void RemovePayers(IEnumerable<ExpensePayer> payers) => _dbContext.ExpensePayers.RemoveRange(payers);

    public void RemoveSplits(IEnumerable<ExpenseSplit> splits) => _dbContext.ExpenseSplits.RemoveRange(splits);

    public void AddPayers(IEnumerable<ExpensePayer> payers) => _dbContext.ExpensePayers.AddRange(payers);

    public void AddSplits(IEnumerable<ExpenseSplit> splits) => _dbContext.ExpenseSplits.AddRange(splits);
}
