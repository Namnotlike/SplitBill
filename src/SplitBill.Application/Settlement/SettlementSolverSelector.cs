namespace SplitBill.Application.Settlement;

/// <summary>Quy tắc chọn solver theo CLAUDE.md mục 6.3.</summary>
public static class SettlementSolverSelector
{
    public static ISettlementSolver Choose(int nonZeroMemberCount) =>
        Choose(nonZeroMemberCount, new GreedySettlementSolver());

    /// <summary>
    /// Cho phép truyền sẵn 1 <see cref="GreedySettlementSolver"/> đã cấu hình tie-break strategy —
    /// dùng bởi <see cref="SocialSettlementPlanner"/> (mục 6.4) để cùng 1 strategy được áp dụng nhất
    /// quán dù chọn Optimal (giải từng nhóm con bằng greedy fallback) hay Greedy trực tiếp.
    /// </summary>
    public static ISettlementSolver Choose(int nonZeroMemberCount, GreedySettlementSolver greedy) =>
        Choose(nonZeroMemberCount, greedy, Array.Empty<(Guid, Guid)>());

    /// <summary>
    /// Như trên, đồng thời cho <see cref="OptimalSettlementSolver"/> biết pastPairs để DP phân hoạch
    /// cũng ưu tiên gộp các cặp quen thuộc vào chung 1 nhóm (mục 6.4) — xem ghi chú tại
    /// <see cref="OptimalSettlementSolver"/>. Không ảnh hưởng gì khi <paramref name="pastPairs"/> rỗng.
    /// </summary>
    public static ISettlementSolver Choose(
        int nonZeroMemberCount, GreedySettlementSolver greedy, IReadOnlyCollection<(Guid MemberA, Guid MemberB)> pastPairs) =>
        nonZeroMemberCount <= 15 ? new OptimalSettlementSolver(greedy, pastPairs) : greedy;
}
