using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class PushSubscriptionRepository : IPushSubscriptionRepository
{
    private readonly SplitBillDbContext _dbContext;

    public PushSubscriptionRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken cancellationToken) =>
        _dbContext.PushSubscriptions.FirstOrDefaultAsync(p => p.Endpoint == endpoint, cancellationToken);

    public async Task AddAsync(PushSubscription subscription, CancellationToken cancellationToken) =>
        await _dbContext.PushSubscriptions.AddAsync(subscription, cancellationToken);

    public Task<List<PushSubscription>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.PushSubscriptions.Where(p => p.UserId == userId).ToListAsync(cancellationToken);

    public Task DeleteAsync(PushSubscription subscription, CancellationToken cancellationToken)
    {
        _dbContext.PushSubscriptions.Remove(subscription);
        return Task.CompletedTask;
    }
}
