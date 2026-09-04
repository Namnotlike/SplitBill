using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly SplitBillDbContext _dbContext;

    public AuditLogRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AuditLog log, CancellationToken cancellationToken) =>
        await _dbContext.AuditLogs.AddAsync(log, cancellationToken);

    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedByGroupIdAsync(
        Guid groupId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.AuditLogs
            .Where(a => a.GroupId == groupId)
            .OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
