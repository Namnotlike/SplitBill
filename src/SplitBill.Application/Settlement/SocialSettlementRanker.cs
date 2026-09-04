namespace SplitBill.Application.Settlement;

/// <summary>
/// Cài đặt CLAUDE.md mục 6.4. Trong các phương án có CÙNG số giao dịch tối thiểu, ưu tiên theo thứ
/// tự (lexicographic, tiêu chí sau chỉ xét khi tiêu chí trước hòa — đúng tinh thần "chênh lệch không
/// đáng kể" của mục 3):
/// 1. Càng nhiều giao dịch đi qua cặp (from, to) đã từng có Settlement với nhau càng tốt.
/// 2. Càng ít vi phạm "1 người phải chuyển đi quá 3 lượt" càng tốt (đo bằng tổng phần vượt ngưỡng).
/// 3. Càng nhiều giao dịch có số tiền tròn 1000đ càng tốt.
/// </summary>
public sealed class SocialSettlementRanker : ISettlementRanker
{
    private const int MaxRecommendedOutgoingTransfers = 3;

    public IReadOnlyList<SettlementTransaction> SelectBest(
        IReadOnlyList<IReadOnlyList<SettlementTransaction>> candidates,
        IReadOnlyCollection<(Guid MemberA, Guid MemberB)> pastPairs)
    {
        if (candidates.Count == 0)
        {
            return Array.Empty<SettlementTransaction>();
        }

        var minCount = candidates.Min(c => c.Count);
        var minimal = candidates.Where(c => c.Count == minCount).ToList();
        if (minimal.Count == 1)
        {
            return minimal[0];
        }

        var pastPairSet = pastPairs.Select(Normalize).ToHashSet();

        return minimal
            .Select(plan => (Plan: plan, Score: Score(plan, pastPairSet)))
            .OrderByDescending(x => x.Score.PastPairMatches)
            .ThenBy(x => x.Score.ExcessOverLimit)
            .ThenByDescending(x => x.Score.RoundAmountCount)
            .First().Plan;
    }

    private static (int PastPairMatches, int ExcessOverLimit, int RoundAmountCount) Score(
        IReadOnlyList<SettlementTransaction> plan, HashSet<(Guid, Guid)> pastPairSet)
    {
        var pastPairMatches = plan.Count(t => pastPairSet.Contains(Normalize((t.FromMemberId, t.ToMemberId))));

        var outgoingCounts = plan.GroupBy(t => t.FromMemberId).ToDictionary(g => g.Key, g => g.Count());
        var excessOverLimit = outgoingCounts.Values.Sum(c => Math.Max(0, c - MaxRecommendedOutgoingTransfers));

        var roundAmountCount = plan.Count(t => t.Amount % 1000 == 0);

        return (pastPairMatches, excessOverLimit, roundAmountCount);
    }

    private static (Guid, Guid) Normalize((Guid A, Guid B) pair) =>
        string.CompareOrdinal(pair.A.ToString(), pair.B.ToString()) <= 0 ? (pair.A, pair.B) : (pair.B, pair.A);
}
