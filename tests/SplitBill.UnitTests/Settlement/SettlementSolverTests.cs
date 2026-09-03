using System.Diagnostics;
using FluentAssertions;
using SplitBill.Application.Settlement;
using Xunit;

namespace SplitBill.UnitTests.Settlement;

/// <summary>Test cases bắt buộc ở CLAUDE.md mục 7.3 (thuật toán settlement) + invariant chung.</summary>
public sealed class SettlementSolverTests
{
    private readonly GreedySettlementSolver _greedy = new();
    private readonly OptimalSettlementSolver _optimal = new();

    // S1: A:+100, B:-100 → 1
    [Fact]
    public void S1_SimpleCase_OneTransaction()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var balances = new[] { new MemberBalance(a, 100), new MemberBalance(b, -100) };

        var result = _optimal.Solve(balances);

        result.Should().HaveCount(1);
        AssertInvariants(balances, result);
    }

    // S2: A:+100, B:-50, C:-50 → 2
    [Fact]
    public void S2_OneCreditorTwoDebtors_TwoTransactions()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var balances = new[] { new MemberBalance(a, 100), new MemberBalance(b, -50), new MemberBalance(c, -50) };

        var result = _optimal.Solve(balances);

        result.Should().HaveCount(2);
        AssertInvariants(balances, result);
    }

    // S3: A:+50, B:-50, C:+30, D:-30 → 2 (tách được 2 nhóm tổng-0 {A,B} và {C,D})
    [Fact]
    public void S3_TwoIndependentZeroSumGroups_TwoTransactions()
    {
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var balances = new[]
        {
            new MemberBalance(a, 50), new MemberBalance(b, -50),
            new MemberBalance(c, 30), new MemberBalance(d, -30),
        };

        var result = _optimal.Solve(balances);

        result.Should().HaveCount(2);
        AssertInvariants(balances, result);
    }

    // S4: A:+40, B:+30, C:-70, D:+20, E:-20 → 3 (tách được nhóm {D,E})
    [Fact]
    public void S4_ThreeCreditorsTwoDebtors_ThreeTransactions()
    {
        var (a, b, c, d, e) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var balances = new[]
        {
            new MemberBalance(a, 40), new MemberBalance(b, 30), new MemberBalance(c, -70),
            new MemberBalance(d, 20), new MemberBalance(e, -20),
        };

        var result = _optimal.Solve(balances);

        result.Should().HaveCount(3);
        AssertInvariants(balances, result);
    }

    // S5: tất cả net = 0 → 0 giao dịch
    [Fact]
    public void S5_AllZero_NoTransactions()
    {
        var balances = new[] { new MemberBalance(Guid.NewGuid(), 0), new MemberBalance(Guid.NewGuid(), 0) };

        _optimal.Solve(balances).Should().BeEmpty();
        _greedy.Solve(balances).Should().BeEmpty();
    }

    // S6: 12 người net ngẫu nhiên tổng 0
    [Fact]
    public void S6_TwelveRandomMembers_InvariantsHold()
    {
        var random = new Random(42);
        var balances = RandomZeroSumBalances(random, 12, maxAbs: 500_000);

        var result = _optimal.Solve(balances);

        AssertInvariants(balances, result);
        result.Count.Should().BeLessThanOrEqualTo(balances.Count(b => b.Net != 0) - 1);
    }

    // S7: 30 người → phải fallback greedy, chạy < 100ms
    [Fact]
    public void S7_ThirtyMembers_FallsBackToGreedy_RunsFast()
    {
        var random = new Random(7);
        var balances = RandomZeroSumBalances(random, 30, maxAbs: 1_000_000);

        var stopwatch = Stopwatch.StartNew();
        var result = _optimal.Solve(balances);
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
        AssertInvariants(balances, result);

        // n=30 > 15 → SettlementSolverSelector phải chọn Greedy, không phải DP bitmask.
        SettlementSolverSelector.Choose(balances.Count(b => b.Net != 0)).Should().BeOfType<GreedySettlementSolver>();
    }

    // S3b: chứng minh bằng thực nghiệm (không suy luận tay) rằng OptimalSettlementSolver cho kết
    // quả không tệ hơn GreedySettlementSolver, và có ít nhất 1 trường hợp thực sự tốt hơn hẳn —
    // xem cảnh báo đã sửa ở CLAUDE.md mục 7.3.
    [Fact]
    public void S3b_OptimalIsNeverWorseThanGreedy_AndSometimesStrictlyBetter()
    {
        var random = new Random(2026);
        var foundStrictImprovement = false;

        for (var trial = 0; trial < 300; trial++)
        {
            var balances = RandomZeroSumBalances(random, memberCount: 7, maxAbs: 100);
            if (balances.All(b => b.Net == 0))
            {
                continue;
            }

            var greedyResult = _greedy.Solve(balances);
            var optimalResult = _optimal.Solve(balances);

            AssertInvariants(balances, greedyResult);
            AssertInvariants(balances, optimalResult);

            optimalResult.Count.Should().BeLessThanOrEqualTo(greedyResult.Count,
                "OptimalSettlementSolver không bao giờ được tệ hơn Greedy");

            if (optimalResult.Count < greedyResult.Count)
            {
                foundStrictImprovement = true;
            }
        }

        foundStrictImprovement.Should().BeTrue(
            "phải tồn tại ít nhất 1 trường hợp Optimal tốt hơn Greedy, nếu không DP bitmask ở mục 6.3 là thừa");
    }

    private static MemberBalance[] RandomZeroSumBalances(Random random, int memberCount, int maxAbs)
    {
        var ids = Enumerable.Range(0, memberCount).Select(_ => Guid.NewGuid()).ToArray();
        var nets = new long[memberCount];
        long sum = 0;
        for (var i = 0; i < memberCount - 1; i++)
        {
            nets[i] = random.Next(-maxAbs, maxAbs + 1);
            sum += nets[i];
        }

        nets[memberCount - 1] = -sum;

        return ids.Select((id, i) => new MemberBalance(id, nets[i])).ToArray();
    }

    /// <summary>Invariant bắt buộc cho mọi kết quả settlement (CLAUDE.md mục 7.3).</summary>
    private static void AssertInvariants(IReadOnlyList<MemberBalance> balances, IReadOnlyList<SettlementTransaction> transactions)
    {
        transactions.Should().OnlyContain(t => t.Amount > 0);
        transactions.Should().OnlyContain(t => t.FromMemberId != t.ToMemberId);

        var net = balances.ToDictionary(b => b.MemberId, b => b.Net);
        foreach (var t in transactions)
        {
            // FromMemberId (con nợ) trả tiền → net tiến về 0 từ phía âm → cộng.
            // ToMemberId (chủ nợ) nhận tiền → net tiến về 0 từ phía dương → trừ.
            net[t.FromMemberId] += t.Amount;
            net[t.ToMemberId] -= t.Amount;
        }

        net.Values.Should().OnlyContain(v => v == 0);

        var nonZeroCount = balances.Count(b => b.Net != 0);
        transactions.Count.Should().BeLessThanOrEqualTo(Math.Max(0, nonZeroCount - 1));
    }
}
