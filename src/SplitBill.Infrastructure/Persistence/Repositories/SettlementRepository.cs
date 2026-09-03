using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
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

    public async Task AddAsync(SettlementEntity settlement, CancellationToken cancellationToken) =>
        await _dbContext.Settlements.AddAsync(settlement, cancellationToken);
}
