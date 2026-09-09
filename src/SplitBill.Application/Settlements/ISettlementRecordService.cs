namespace SplitBill.Application.Settlements;

/// <summary>`POST /groups/{id}/settlements`, `/confirm`, `/reject`, `DELETE` (CLAUDE.md mục 8).</summary>
public interface ISettlementRecordService
{
    Task<SettlementDto> CreateAsync(Guid callerUserId, Guid groupId, CreateSettlementRequest request, CancellationToken cancellationToken);
    Task<SettlementDto> ConfirmAsync(Guid callerUserId, Guid settlementId, CancellationToken cancellationToken);
    Task<SettlementDto> RejectAsync(Guid callerUserId, Guid settlementId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid callerUserId, Guid settlementId, CancellationToken cancellationToken);

    /// <summary>Danh sách settlement ĐÃ xóa (soft-delete) của nhóm — CLAUDE.md mục 24.</summary>
    Task<IReadOnlyList<SettlementDto>> GetDeletedAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Khôi phục 1 settlement đã xóa — CLAUDE.md mục 24. Ném <c>SETTLEMENT_NOT_DELETED</c>
    /// nếu settlement hiện KHÔNG ở trạng thái đã xóa.</summary>
    Task<SettlementDto> RestoreAsync(Guid callerUserId, Guid settlementId, CancellationToken cancellationToken);

    /// <summary>Danh sách settlement chưa xóa của nhóm — bổ sung khi làm frontend (2026-09-03),
    /// CLAUDE.md mục 8 ban đầu không có endpoint liệt kê, gây thiếu dữ liệu để UI hiển thị các
    /// khoản đang chờ xác nhận.</summary>
    Task<IReadOnlyList<SettlementDto>> GetByGroupAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);
}
