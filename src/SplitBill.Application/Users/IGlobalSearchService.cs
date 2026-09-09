namespace SplitBill.Application.Users;

/// <summary>CLAUDE.md mục 25.8 — Tìm kiếm xuyên nhóm.</summary>
public interface IGlobalSearchService
{
    Task<GlobalSearchResultDto> SearchAsync(Guid callerUserId, string? query, CancellationToken cancellationToken);
}
