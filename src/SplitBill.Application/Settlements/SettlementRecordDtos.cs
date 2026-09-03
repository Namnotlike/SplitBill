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
    DateTimeOffset CreatedAt);

public sealed record CreateSettlementRequest(Guid FromMemberId, Guid ToMemberId, long Amount, string? Note = null);
