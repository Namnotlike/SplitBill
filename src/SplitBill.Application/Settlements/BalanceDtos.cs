namespace SplitBill.Application.Settlements;

public sealed record MemberBalanceDto(Guid MemberId, string MemberName, long Net);

public sealed record VietQrDto(string BankBin, string AccountNumber, long Amount, string Content, string Payload);

public sealed record SettlementTransactionDto(
    Guid FromMemberId,
    string FromName,
    Guid ToMemberId,
    string ToName,
    long Amount,
    VietQrDto? VietQr);

public sealed record SettlementPlanDto(bool Simplified, int TransactionCount, IReadOnlyList<SettlementTransactionDto> Transactions);
