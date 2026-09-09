using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class GroupTemplateRepository : IGroupTemplateRepository
{
    private readonly SplitBillDbContext _dbContext;

    public GroupTemplateRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(GroupTemplate template, CancellationToken cancellationToken) =>
        await _dbContext.GroupTemplates.AddAsync(template, cancellationToken);

    public Task<List<GroupTemplate>> GetByCreatedByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.GroupTemplates.Where(t => t.CreatedByUserId == userId).ToListAsync(cancellationToken);

    public Task<GroupTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.GroupTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
}
