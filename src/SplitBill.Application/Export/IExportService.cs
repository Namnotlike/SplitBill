namespace SplitBill.Application.Export;

/// <summary>Xuất dữ liệu nhóm dạng CSV (CLAUDE.md mục M5 — bổ sung 2026-09-04 theo yêu cầu người dùng:
/// CSV danh sách khoản chi + số dư, không cần Excel/PDF thật).</summary>
public interface IExportService
{
    Task<string> ExportExpensesCsvAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    Task<string> ExportBalancesCsvAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);
}
