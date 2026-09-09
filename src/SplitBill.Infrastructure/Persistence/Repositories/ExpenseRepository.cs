using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Expenses;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;

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

    public Task<Expense?> GetByIdIncludingDeletedAsync(Guid expenseId, CancellationToken cancellationToken) =>
        _dbContext.Expenses
            .IgnoreQueryFilters()
            .Include(e => e.Payers)
            .Include(e => e.Splits)
            .FirstOrDefaultAsync(e => e.Id == expenseId, cancellationToken);

    public Task<List<Expense>> GetDeletedByGroupIdAsync(Guid groupId, CancellationToken cancellationToken) =>
        _dbContext.Expenses
            .IgnoreQueryFilters()
            .Where(e => e.GroupId == groupId && e.IsDeleted)
            .Include(e => e.Payers)
            .Include(e => e.Splits)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedAsync(
        Guid groupId, int page, int pageSize, ExpenseFilter filter, CancellationToken cancellationToken)
    {
        var query = _dbContext.Expenses.Where(e => e.GroupId == groupId);

        // CLAUDE.md mục 15.2 — mỗi điều kiện chỉ áp dụng khi field tương ứng trong filter có giá trị,
        // giữ nguyên hành vi cũ (không lọc gì) khi gọi với ExpenseFilter.Empty.
        if (!string.IsNullOrWhiteSpace(filter.Title))
        {
            // Dùng ToLower().Contains() thay vì EF.Functions.Like: dịch được cả sang SQL Server lẫn
            // EF Core InMemory (dùng cho unit/integration test — CLAUDE.md mục 2), không phân biệt
            // hoa/thường ở cả hai provider.
            var titleLower = filter.Title.ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(titleLower));
        }
        if (filter.PayerMemberId is { } payerMemberId)
        {
            query = query.Where(e => e.Payers.Any(p => p.GroupMemberId == payerMemberId));
        }
        if (filter.FromDate is { } fromDate)
        {
            query = query.Where(e => e.OccurredAt >= fromDate);
        }
        if (filter.ToDate is { } toDate)
        {
            query = query.Where(e => e.OccurredAt <= toDate);
        }
        if (filter.MinAmount is { } minAmount)
        {
            query = query.Where(e => e.TotalAmount >= minAmount);
        }
        if (filter.MaxAmount is { } maxAmount)
        {
            query = query.Where(e => e.TotalAmount <= maxAmount);
        }
        // Category tên enum không hợp lệ (typo trong query string) coi như "không lọc" thay vì lỗi —
        // đã validate nghiêm ngặt ở ExpenseService lúc tạo/sửa (CLAUDE.md mục 15.3), tầng lọc chỉ cần
        // khoan dung.
        if (!string.IsNullOrWhiteSpace(filter.Category) && Enum.TryParse<ExpenseCategory>(filter.Category, ignoreCase: true, out var category))
        {
            query = query.Where(e => e.Category == category);
        }

        var ordered = query.OrderByDescending(e => e.OccurredAt);

        var total = await ordered.CountAsync(cancellationToken);
        var items = await ordered
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
