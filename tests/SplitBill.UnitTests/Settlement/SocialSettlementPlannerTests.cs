using FluentAssertions;
using SplitBill.Application.Settlement;
using Xunit;

namespace SplitBill.UnitTests.Settlement;

/// <summary>
/// Test end-to-end cho <see cref="SocialSettlementPlanner"/> (CLAUDE.md mục 6.4): sinh nhiều phương
/// án bằng các tie-break strategy khác nhau rồi dùng <see cref="ISettlementRanker"/> chọn phương án
/// tốt nhất — kiểm tra pipeline đầy đủ, không chỉ từng thành phần riêng lẻ.
/// </summary>
public sealed class SocialSettlementPlannerTests
{
    private readonly SocialSettlementPlanner _planner = new(new SocialSettlementRanker());

    [Fact]
    public void Plan_AllZero_ReturnsEmpty()
    {
        var balances = new[] { new MemberBalance(Guid.NewGuid(), 0), new MemberBalance(Guid.NewGuid(), 0) };

        _planner.Plan(balances, []).Should().BeEmpty();
    }

    [Fact]
    public void Plan_WithTieAndPastPair_PrefersPlanRoutedThroughPastPair()
    {
        // Giống kịch bản ở GreedySettlementSolverTieBreakTests: X, Y hòa điểm; P, Q hòa điểm.
        // X đã từng chuyển tiền cho Q. Cả 2 cách ghép (X-Q,Y-P) hay (X-P,Y-Q) đều tối thiểu 2 giao
        // dịch — planner phải chọn đúng cách đi qua cặp đã quen (X→Q), không phụ thuộc GUID ngẫu nhiên.
        var (x, y, p, q) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var balances = new[]
        {
            new MemberBalance(x, -100), new MemberBalance(y, -100),
            new MemberBalance(p, 100), new MemberBalance(q, 100),
        };
        var pastPairs = new[] { (x, q) };

        var result = _planner.Plan(balances, pastPairs);

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.FromMemberId == x && t.ToMemberId == q && t.Amount == 100);
        result.Should().Contain(t => t.FromMemberId == y && t.ToMemberId == p && t.Amount == 100);
    }

    [Fact]
    public void Plan_NeverProducesMoreTransactionsThanPlainOptimalSolver()
    {
        var random = new Random(2026_09_05);

        for (var trial = 0; trial < 100; trial++)
        {
            var balances = RandomZeroSumBalances(random, memberCount: 8, maxAbs: 500);
            if (balances.All(b => b.Net == 0))
            {
                continue;
            }

            var baseline = new OptimalSettlementSolver().Solve(balances);
            var socialPlan = _planner.Plan(balances, []);

            AssertInvariants(balances, socialPlan);
            socialPlan.Count.Should().Be(baseline.Count,
                "ràng buộc mềm chỉ được chọn KHÁC ai-trả-ai giữa các phương án tối thiểu, không được làm tăng số giao dịch");
        }
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

    private static void AssertInvariants(IReadOnlyList<MemberBalance> balances, IReadOnlyList<SettlementTransaction> transactions)
    {
        transactions.Should().OnlyContain(t => t.Amount > 0);
        transactions.Should().OnlyContain(t => t.FromMemberId != t.ToMemberId);

        var net = balances.ToDictionary(b => b.MemberId, b => b.Net);
        foreach (var t in transactions)
        {
            net[t.FromMemberId] += t.Amount;
            net[t.ToMemberId] -= t.Amount;
        }

        net.Values.Should().OnlyContain(v => v == 0);
    }
}
