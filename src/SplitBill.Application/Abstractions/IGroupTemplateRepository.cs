using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IGroupTemplateRepository
{
    Task AddAsync(GroupTemplate template, CancellationToken cancellationToken);

    Task<List<GroupTemplate>> GetByCreatedByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<GroupTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
