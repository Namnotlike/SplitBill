namespace SplitBill.Application.Export;

/// <summary>Xuất dữ liệu nhóm dạng CSV (CLAUDE.md mục M5 — bổ sung 2026-09-04 theo yêu cầu người dùng:
/// CSV danh sách khoản chi + số dư, không cần Excel/PDF thật).</summary>
public interface IExportService
{
    Task<string> ExportExpensesCsvAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    Task<string> ExportBalancesCsvAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Xuất/backup toàn bộ dữ liệu tài chính cốt lõi của 1 nhóm dạng JSON (CLAUDE.md mục
    /// 25.4) — xem <see cref="GroupBackupDto"/> để biết phạm vi chính xác.</summary>
    Task<GroupBackupDto> ExportGroupBackupAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);
}
