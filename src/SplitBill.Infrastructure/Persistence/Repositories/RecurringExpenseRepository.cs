using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class RecurringExpenseRepository : IRecurringExpenseRepository
{
    private readonly SplitBillDbContext _dbContext;

    public RecurringExpenseRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<RecurringExpenseTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.RecurringExpenseTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<List<RecurringExpenseTemplate>> GetByGroupIdAsync(Guid groupId, CancellationToken cancellationToken) =>
        _dbContext.RecurringExpenseTemplates.Where(t => t.GroupId == groupId).ToListAsync(cancellationToken);

    public Task<List<RecurringExpenseTemplate>> GetDueTemplatesAsync(DateTimeOffset asOf, CancellationToken cancellationToken) =>
        _dbContext.RecurringExpenseTemplates
            .Where(t => t.IsActive && t.NextRunAt <= asOf)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RecurringExpenseTemplate template, CancellationToken cancellationToken) =>
        await _dbContext.RecurringExpenseTemplates.AddAsync(template, cancellationToken);
}
