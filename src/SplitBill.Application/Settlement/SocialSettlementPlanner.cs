namespace SplitBill.Application.Settlement;

/// <summary>
/// Bước 4 — ràng buộc mềm (CLAUDE.md mục 6.4). Sinh vài phương án ứng viên bằng các
/// <see cref="SettlementTieBreakStrategy"/> khác nhau (mỗi strategy nhắm 1 tiêu chí xã hội), rồi
/// dùng <see cref="ISettlementRanker"/> chọn phương án tốt nhất. Class thuần, không phụ thuộc EF Core
/// hay DbContext — nhận sẵn balances và danh sách cặp đã từng Settlement với nhau từ tầng gọi.
///
/// Chỉ dùng cho chế độ <c>Group.SimplifyDebts = true</c> (gộp nợ); chế độ tắt gộp nợ (mục 6.5) không
/// đi qua planner này.
/// </summary>
public sealed class SocialSettlementPlanner
{
    private readonly ISettlementRanker _ranker;

    public SocialSettlementPlanner(ISettlementRanker ranker)
    {
        _ranker = ranker;
    }

    public IReadOnlyList<SettlementTransaction> Plan(
        IReadOnlyList<MemberBalance> balances,
        IReadOnlyCollection<(Guid MemberA, Guid MemberB)> pastPairs)
    {
        var nonZeroCount = balances.Count(b => b.Net != 0);
        if (nonZeroCount == 0)
        {
            return Array.Empty<SettlementTransaction>();
        }

        SettlementTieBreakStrategy[] strategies =
        [
            SettlementTieBreakStrategy.OrdinalAscending,
            SettlementTieBreakStrategy.PastPairAffinity,
            SettlementTieBreakStrategy.LoadBalancing,
        ];

        var candidates = strategies
            .Select(strategy =>
            {
                var greedy = new GreedySettlementSolver(strategy, pastPairs);
                // Chỉ cho DP phân hoạch (OptimalSettlementSolver) biết pastPairs khi strategy đang
                // nhắm đúng tiêu chí đó — giữ candidate OrdinalAscending làm baseline "trung lập"
                // thật sự để ranker có cơ sở so sánh (LoadBalancing không có tiêu chí ở tầng DP nên
                // cũng không cần truyền).
                var dpPastPairs = strategy == SettlementTieBreakStrategy.PastPairAffinity
                    ? pastPairs
                    : Array.Empty<(Guid, Guid)>();
                return SettlementSolverSelector.Choose(nonZeroCount, greedy, dpPastPairs).Solve(balances);
            })
            .ToList();

        return _ranker.SelectBest(candidates, pastPairs);
    }
}
