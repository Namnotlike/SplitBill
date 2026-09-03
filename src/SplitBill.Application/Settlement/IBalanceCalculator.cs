namespace SplitBill.Application.Settlement;

/// <summary>Tính số dư ròng của mọi thành viên trong nhóm (CLAUDE.md mục 6.1).</summary>
public interface IBalanceCalculator
{
    IReadOnlyList<MemberBalance> Calculate(
        IEnumerable<ExpenseBalanceInput> expenses,
        IEnumerable<SettlementBalanceInput> settlements);
}
