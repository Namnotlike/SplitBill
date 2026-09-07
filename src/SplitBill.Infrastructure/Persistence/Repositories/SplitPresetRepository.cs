using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class SplitPresetRepository : ISplitPresetRepository
{
    private readonly SplitBillDbContext _dbContext;

    public SplitPresetRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(SplitPreset preset, CancellationToken cancellationToken) =>
        await _dbContext.SplitPresets.AddAsync(preset, cancellationToken);

    public Task<List<SplitPreset>> GetByGroupIdAsync(Guid groupId, CancellationToken cancellationToken) =>
        _dbContext.SplitPresets.Where(p => p.GroupId == groupId).ToListAsync(cancellationToken);

    public Task<SplitPreset?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.SplitPresets.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
}
