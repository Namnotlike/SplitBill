using System.Numerics;

namespace SplitBill.Application.Settlement;

/// <summary>
/// Cài đặt CLAUDE.md mục 6.3: DP bitmask tìm số giao dịch tối thiểu tuyệt đối bằng cách tách nhóm
/// con tổng-0 nhiều nhất có thể. Chỉ nên dùng khi số người có net != 0 &lt;= 15 (xem
/// <see cref="SettlementSolverSelector"/>). Bọc timeout 2 giây, quá thời gian thì fallback Greedy.
/// </summary>
public sealed class OptimalSettlementSolver : ISettlementSolver
{
    private static readonly TimeSpan SolveTimeout = TimeSpan.FromSeconds(2);

    private readonly ISettlementSolver _fallback;
    private readonly IReadOnlyCollection<(Guid MemberA, Guid MemberB)> _pastPairs;

    public OptimalSettlementSolver() : this(new GreedySettlementSolver(), Array.Empty<(Guid, Guid)>())
    {
    }

    public OptimalSettlementSolver(ISettlementSolver fallback) : this(fallback, Array.Empty<(Guid, Guid)>())
    {
    }

    /// <param name="fallback">Solver dùng để giải cụ thể từng nhóm con tổng-0 sau khi đã phân hoạch.</param>
    /// <param name="pastPairs">Ràng buộc mềm (CLAUDE.md mục 6.4, tiêu chí 1) — khi phân hoạch có NHIỀU
    /// cách tách đạt cùng số nhóm tổng-0 tối đa, ưu tiên cách tách có nhiều cặp thành viên trong CÙNG
    /// 1 nhóm đã từng có Settlement với nhau. Rỗng thì DP hoạt động y hệt bản gốc (không đổi hành vi).</param>
    public OptimalSettlementSolver(ISettlementSolver fallback, IReadOnlyCollection<(Guid MemberA, Guid MemberB)> pastPairs)
    {
        _fallback = fallback;
        _pastPairs = pastPairs;
    }

    public IReadOnlyList<SettlementTransaction> Solve(IReadOnlyList<MemberBalance> balances)
    {
        var nonZero = balances.Where(b => b.Net != 0)
            .OrderBy(b => b.MemberId.ToString(), StringComparer.Ordinal)
            .ToList();

        var n = nonZero.Count;
        if (n == 0)
        {
            return Array.Empty<SettlementTransaction>();
        }

        if (n > 15)
        {
            return _fallback.Solve(balances);
        }

        using var cts = new CancellationTokenSource(SolveTimeout);
        try
        {
            var groups = FindOptimalPartition(nonZero, cts.Token);
            var transactions = new List<SettlementTransaction>();
            foreach (var groupIndexes in groups)
            {
                var groupBalances = groupIndexes.Select(i => nonZero[i]).ToList();
                transactions.AddRange(_fallback.Solve(groupBalances));
            }

            return transactions;
        }
        catch (OperationCanceledException)
        {
            return _fallback.Solve(balances);
        }
    }

    /// <summary>
    /// Trả về danh sách các nhóm (mỗi nhóm là index trong <paramref name="members"/>) sao cho tổng số
    /// nhóm tổng-0 là nhiều nhất có thể; phần tử không tách được vào nhóm tổng-0 nào được gộp thành
    /// đúng 1 nhóm "leftover" duy nhất ở cuối.
    /// </summary>
    private List<List<int>> FindOptimalPartition(List<MemberBalance> members, CancellationToken cancellationToken)
    {
        var n = members.Count;
        var fullMask = (1 << n) - 1;

        var sum = new long[1 << n];
        for (var mask = 1; mask <= fullMask; mask++)
        {
            var lowBit = mask & -mask;
            var lowIndex = BitOperations.TrailingZeroCount(lowBit);
            sum[mask] = sum[mask ^ lowBit] + members[lowIndex].Net;
        }

        // DP 2 khóa (lexicographic): khóa chính dpCount[mask] = số nhóm tổng-0 nhiều nhất có thể tách
        // (mục 6.3, không đổi so với bản gốc); khóa phụ dpScore[mask] = tổng "điểm cặp quen thuộc"
        // (mục 6.4, tiêu chí 1) LỚN NHẤT đạt được TRONG SỐ các cách tách vẫn giữ nguyên dpCount[mask]
        // tối đa. Khóa phụ phải được CỘNG DỒN qua đệ quy (dpScore[mask^sub] + điểm của sub) chứ không
        // chỉ so sánh điểm của riêng subset đang xét ở mức hiện tại — nếu chỉ so 1 mức, tie-break sẽ
        // bỏ sót trường hợp thành viên có cặp quen thuộc không phải là "lowBit" (đại diện bắt buộc)
        // của mask hiện tại, dẫn tới kết quả không xác định (đã phát hiện qua test integration
        // GetSettlementPlanAsync_WithTieAndPastSettlement_RoutesThroughPastPair chạy flaky trước khi
        // sửa thành 2 khóa — xem CLAUDE.md mục 6.4).
        var dpCount = new int[1 << n];
        var dpScore = new int[1 << n];
        // choice[mask]: 0 nghĩa là "bit thấp nhất của mask là leftover, dp[mask] = dp[mask ^ lowBit]".
        // Khác 0 nghĩa là subset (chứa bit thấp nhất) được tách thành 1 nhóm tổng-0.
        var choice = new int[1 << n];

        for (var mask = 1; mask <= fullMask; mask++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lowBit = mask & -mask;
            var maskWithoutLowBit = mask ^ lowBit;

            // Nhánh 1: bit thấp nhất KHÔNG tách vào nhóm tổng-0 nào (bắt buộc phải xét — xem
            // ghi chú sửa lỗi ở CLAUDE.md mục 6.3).
            var bestCount = dpCount[maskWithoutLowBit];
            var bestScore = dpScore[maskWithoutLowBit];
            var bestChoice = 0;

            // Nhánh 2: thử mọi tập con chứa bit thấp nhất, có tổng = 0, tách thành 1 nhóm.
            for (var sub = mask; sub > 0; sub = (sub - 1) & mask)
            {
                if ((sub & lowBit) == 0 || sum[sub] != 0)
                {
                    continue;
                }

                var remaining = mask ^ sub;
                var candidateCount = dpCount[remaining] + 1;
                var candidateScore = dpScore[remaining] + SubsetPastPairScore(sub, members);

                if (candidateCount > bestCount ||
                    (candidateCount == bestCount && candidateScore > bestScore))
                {
                    bestCount = candidateCount;
                    bestScore = candidateScore;
                    bestChoice = sub;
                }
            }

            dpCount[mask] = bestCount;
            dpScore[mask] = bestScore;
            choice[mask] = bestChoice;
        }

        var groups = new List<List<int>>();
        var leftover = new List<int>();

        var current = fullMask;
        while (current != 0)
        {
            var lowBit = current & -current;
            var sub = choice[current];
            if (sub == 0)
            {
                leftover.Add(BitOperations.TrailingZeroCount(lowBit));
                current ^= lowBit;
            }
            else
            {
                groups.Add(BitsToIndexes(sub, n));
                current ^= sub;
            }
        }

        if (leftover.Count > 0)
        {
            groups.Add(leftover);
        }

        return groups;
    }

    /// <summary>Số cặp thành viên TRONG CÙNG subset đã từng có Settlement với nhau (mục 6.4, tiêu chí
    /// 1). subsetMask = 0 (leftover) luôn cho điểm 0.</summary>
    private int SubsetPastPairScore(int subsetMask, List<MemberBalance> members)
    {
        if (subsetMask == 0 || _pastPairs.Count == 0)
        {
            return 0;
        }

        var indexes = BitsToIndexes(subsetMask, members.Count);
        var score = 0;
        for (var i = 0; i < indexes.Count; i++)
        {
            for (var j = i + 1; j < indexes.Count; j++)
            {
                var a = members[indexes[i]].MemberId;
                var b = members[indexes[j]].MemberId;
                if (_pastPairs.Any(p => (p.MemberA == a && p.MemberB == b) || (p.MemberA == b && p.MemberB == a)))
                {
                    score++;
                }
            }
        }

        return score;
    }

    private static List<int> BitsToIndexes(int mask, int n)
    {
        var list = new List<int>();
        for (var i = 0; i < n; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                list.Add(i);
            }
        }

        return list;
    }
}
