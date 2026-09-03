using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken cancellationToken);
}
