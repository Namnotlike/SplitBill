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

    public OptimalSettlementSolver() : this(new GreedySettlementSolver())
    {
    }

    public OptimalSettlementSolver(ISettlementSolver fallback)
    {
        _fallback = fallback;
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
    private static List<List<int>> FindOptimalPartition(List<MemberBalance> members, CancellationToken cancellationToken)
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

        var dp = new int[1 << n];
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
            var best = dp[maskWithoutLowBit];
            var bestChoice = 0;

            // Nhánh 2: thử mọi tập con chứa bit thấp nhất, có tổng = 0, tách thành 1 nhóm.
            for (var sub = mask; sub > 0; sub = (sub - 1) & mask)
            {
                if ((sub & lowBit) == 0 || sum[sub] != 0)
                {
                    continue;
                }

                var candidate = dp[mask ^ sub] + 1;
                if (candidate > best)
                {
                    best = candidate;
                    bestChoice = sub;
                }
            }

            dp[mask] = best;
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
