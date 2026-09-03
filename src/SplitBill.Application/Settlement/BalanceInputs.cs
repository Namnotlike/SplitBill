using SplitBill.Application.Splitting;
using SplitBill.Domain.Enums;

namespace SplitBill.Application.Settlement;

/// <summary>
/// Dữ liệu thô của một expense chưa xóa, dùng để tính balance. <see cref="Splits"/> là
/// ExpenseSplit ĐÃ lưu trong DB (đã gồm phần ExtraFeeAmount, chưa gồm delta ảo — xem BalanceCalculator).
/// </summary>
public sealed record ExpenseBalanceInput(
    Guid ExpenseId,
    IReadOnlyList<MemberAmount> Payers,
    IReadOnlyList<MemberAmount> Splits);

/// <summary>Dữ liệu thô của một settlement chưa xóa, dùng để tính balance.</summary>
public sealed record SettlementBalanceInput(
    Guid FromMemberId,
    Guid ToMemberId,
    long Amount,
    SettlementStatus Status);
