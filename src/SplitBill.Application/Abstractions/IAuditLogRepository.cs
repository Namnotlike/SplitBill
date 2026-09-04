using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken cancellationToken);

    /// <summary>Phân trang lịch sử thay đổi của 1 nhóm, mới nhất trước (CLAUDE.md mục 8).</summary>
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedByGroupIdAsync(
        Guid groupId, int page, int pageSize, CancellationToken cancellationToken);
}
