using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IPushSubscriptionRepository
{
    Task<PushSubscription?> GetByEndpointAsync(string endpoint, CancellationToken cancellationToken);

    Task AddAsync(PushSubscription subscription, CancellationToken cancellationToken);

    Task<List<PushSubscription>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task DeleteAsync(PushSubscription subscription, CancellationToken cancellationToken);
}
