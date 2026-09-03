namespace SplitBill.Application.Settlement;

/// <summary>Một lượt chuyển tiền đề xuất bởi solver: FromMemberId trả cho ToMemberId số tiền Amount.</summary>
public sealed record SettlementTransaction(Guid FromMemberId, Guid ToMemberId, long Amount);
