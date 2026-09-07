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

/// <summary>1 dòng "khoản liên quan tới 1 người, ở 1 nhóm cụ thể" trong widget "Ai đang nợ tôi" (CLAUDE.md
/// mục 20, bổ sung 2026-09-07). Amount dương = người đó nợ tôi, âm = tôi nợ người đó — lấy thẳng từ
/// giao dịch trong settlement-plan CỦA NHÓM ĐÓ có tôi là 1 trong 2 bên, không phải hiệu số Σ net.</summary>
public sealed record CounterpartyGroupAmountDto(Guid GroupId, string GroupName, string Currency, long Amount);

/// <summary>Tổng hợp "ai đang nợ tôi / tôi đang nợ ai" XUYÊN NHÓM, gộp theo TỪNG NGƯỜI (CLAUDE.md mục
/// 20) — khác <see cref="PersonalGroupBalanceDto"/> (mục 15.4) vốn gộp theo TỪNG NHÓM. Chỉ áp dụng
/// cho counterparty có tài khoản (UserId khác null) — khách vãng lai không có danh tính ổn định xuyên
/// nhóm nên không xuất hiện ở đây (giao dịch của họ vẫn xem được bình thường ở settlement-plan của
/// đúng nhóm đó). KHÔNG cộng gộp <see cref="CounterpartyGroupAmountDto.Amount"/> thành 1 số duy nhất
/// — mỗi nhóm có thể dùng 1 loại tiền tệ khác nhau (mục 14), giữ nguyên danh sách riêng theo nhóm.</summary>
public sealed record CounterpartyBalanceDto(
    Guid CounterpartyUserId,
    string CounterpartyDisplayName,
    IReadOnlyList<CounterpartyGroupAmountDto> Groups);
