namespace SplitBill.Application.Settlements;

public sealed record SettlementDto(
    Guid Id,
    Guid GroupId,
    Guid FromMemberId,
    Guid ToMemberId,
    long Amount,
    string Status,
    string? Note,
    Guid RecordedByMemberId,
    Guid? ConfirmedByMemberId,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset CreatedAt,
    // Miễn nợ (CLAUDE.md mục 25.2) — true khi settlement này được tạo trực tiếp ở trạng thái
    // Confirmed KHÔNG qua chuyển tiền thật, do chủ nợ (ToMemberId) chủ động xóa nợ.
    bool IsWaived = false);

public sealed record CreateSettlementRequest(Guid FromMemberId, Guid ToMemberId, long Amount, string? Note = null);

/// <summary>Miễn nợ (CLAUDE.md mục 25.2) — tạo trực tiếp 1 settlement Confirmed không qua bước chuyển
/// tiền thật. Chỉ chủ nợ (ToMemberId, người đang được nợ) mới gọi được — xem
/// <see cref="ISettlementRecordService.WaiveAsync"/>.</summary>
public sealed record WaiveSettlementRequest(Guid FromMemberId, Guid ToMemberId, long Amount, string? Note = null);
