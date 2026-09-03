namespace SplitBill.Application.Settlement;

/// <summary>Sinh danh sách giao dịch tối thiểu để mọi thành viên về số dư 0 (CLAUDE.md mục 6).</summary>
public interface ISettlementSolver
{
    IReadOnlyList<SettlementTransaction> Solve(IReadOnlyList<MemberBalance> balances);
}
