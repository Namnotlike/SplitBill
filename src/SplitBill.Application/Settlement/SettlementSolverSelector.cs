namespace SplitBill.Application.Settlement;

/// <summary>Quy tắc chọn solver theo CLAUDE.md mục 6.3.</summary>
public static class SettlementSolverSelector
{
    public static ISettlementSolver Choose(int nonZeroMemberCount) =>
        nonZeroMemberCount <= 15 ? new OptimalSettlementSolver() : new GreedySettlementSolver();
}
