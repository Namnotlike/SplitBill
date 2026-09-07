using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface ISplitPresetRepository
{
    Task AddAsync(SplitPreset preset, CancellationToken cancellationToken);

    Task<List<SplitPreset>> GetByGroupIdAsync(Guid groupId, CancellationToken cancellationToken);

    Task<SplitPreset?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
