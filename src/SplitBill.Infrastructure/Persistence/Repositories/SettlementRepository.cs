using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Enums;
using SettlementEntity = SplitBill.Domain.Entities.Settlement;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class SettlementRepository : ISettlementRepository
{
    private readonly SplitBillDbContext _dbContext;

    public SettlementRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<SettlementEntity>> GetAllByGroupIdAsync(Guid groupId, CancellationToken cancellationToken) =>
        _dbContext.Settlements.Where(s => s.GroupId == groupId).ToListAsync(cancellationToken);

    public Task<SettlementEntity?> GetByIdAsync(Guid settlementId, CancellationToken cancellationToken) =>
        _dbContext.Settlements.FirstOrDefaultAsync(s => s.Id == settlementId, cancellationToken);

    public Task<SettlementEntity?> GetByIdIncludingDeletedAsync(Guid settlementId, CancellationToken cancellationToken) =>
        _dbContext.Settlements.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == settlementId, cancellationToken);

    public Task<List<SettlementEntity>> GetDeletedByGroupIdAsync(Guid groupId, CancellationToken cancellationToken) =>
        _dbContext.Settlements
            .IgnoreQueryFilters()
            .Where(s => s.GroupId == groupId && s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SettlementEntity settlement, CancellationToken cancellationToken) =>
        await _dbContext.Settlements.AddAsync(settlement, cancellationToken);

    public Task<List<SettlementEntity>> GetPendingDueForReminderAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        _dbContext.Settlements
            .Where(s => s.Status == SettlementStatus.Pending && (s.LastReminderSentAt ?? s.CreatedAt) <= cutoff)
            .ToListAsync(cancellationToken);
}
