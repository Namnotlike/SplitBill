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

/// <summary>Số dư của người dùng hiện tại trong MỘT nhóm — dùng cho bảng tổng quan cá nhân ở trang
/// chủ (CLAUDE.md mục 15.4, bổ sung 2026-09-05). Không cộng gộp <see cref="Net"/> giữa các dòng khác
/// nhóm: mỗi nhóm có thể dùng một <see cref="Currency"/> khác nhau (CLAUDE.md mục 14), cộng trực tiếp
/// các Net khác đơn vị tiền sẽ ra một con số vô nghĩa.</summary>
public sealed record PersonalGroupBalanceDto(Guid GroupId, string GroupName, string Currency, long Net);
