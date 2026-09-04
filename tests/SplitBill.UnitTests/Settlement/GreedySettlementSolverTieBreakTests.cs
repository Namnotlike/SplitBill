using FluentAssertions;
using SplitBill.Application.Settlement;
using Xunit;

namespace SplitBill.UnitTests.Settlement;

/// <summary>
/// Test cho <see cref="SettlementTieBreakStrategy"/> — nền tảng cho ràng buộc mềm ở CLAUDE.md mục
/// 6.4. Các test này cố tình dựng tình huống có HÒA ĐIỂM thật (2 debtor cùng Remaining, 2 creditor
/// cùng Remaining) để chứng minh strategy thực sự đổi được AI CHUYỂN CHO AI, không chỉ đổi thứ tự
/// liệt kê — nếu không có hòa điểm thì mọi strategy cho kết quả giống hệt nhau (đã verify ở test
/// cuối file).
/// </summary>
public sealed class GreedySettlementSolverTieBreakTests
{
    [Fact]
    public void PastPairAffinity_WithTiedDebtorsAndCreditors_RoutesThroughPastPairFirst()
    {
        var (x, y, p, q) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var balances = new[]
        {
            new MemberBalance(x, -100), new MemberBalance(y, -100),
            new MemberBalance(p, 100), new MemberBalance(q, 100),
        };

        // X đã từng chuyển tiền với Q (không phải P) — kỳ vọng: X→Q, Y→P, bất kể GUID ordinal ra sao.
        var pastPairs = new[] { (x, q) };
        var solver = new GreedySettlementSolver(SettlementTieBreakStrategy.PastPairAffinity, pastPairs);

        var result = solver.Solve(balances);

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.FromMemberId == x && t.ToMemberId == q && t.Amount == 100);
        result.Should().Contain(t => t.FromMemberId == y && t.ToMemberId == p && t.Amount == 100);
    }

    [Fact]
    public void LoadBalancing_PrefersDebtorWithFewerOutgoingTransactionsSoFar()
    {
        // heavy trả cho c1(15) rồi c2(15) trước — không hòa điểm ở 2 vòng đầu vì 40 rồi 25 luôn lớn
        // hơn light(10) — nên 2 giao dịch đầu chắc chắn thuộc về heavy, tích lũy outgoingCount[heavy]=2.
        // Sau đó heavy còn lại đúng 10, hòa điểm thật với light (cũng còn 10) ở vòng thứ 3, đồng thời
        // c3/c4 (cùng 10) cũng hòa điểm. Đây là điểm quyết định: LoadBalancing phải ưu tiên light
        // (0 giao dịch) hơn heavy (2 giao dịch) — nếu không có LoadBalancing, ai được chọn ở vòng này
        // phụ thuộc ngẫu nhiên vào thứ tự ordinal của GUID (Guid.NewGuid() mỗi lần chạy khác nhau).
        var (heavy, light, c1, c2, c3, c4) = (
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var balances = new[]
        {
            new MemberBalance(heavy, -40),
            new MemberBalance(light, -10),
            new MemberBalance(c1, 15),
            new MemberBalance(c2, 15),
            new MemberBalance(c3, 10),
            new MemberBalance(c4, 10),
        };

        var solver = new GreedySettlementSolver(SettlementTieBreakStrategy.LoadBalancing, Array.Empty<(Guid, Guid)>());
        var result = solver.Solve(balances);

        AssertInvariants(balances, result);

        // light phải là người trả ở vòng hòa điểm (đúng 1 giao dịch, 10đ); heavy gánh 3 giao dịch còn
        // lại (15 + 15 + 10 = 40, khớp tổng nợ).
        result.Count(t => t.FromMemberId == light).Should().Be(1);
        result.Should().Contain(t => t.FromMemberId == light && t.Amount == 10);
        result.Count(t => t.FromMemberId == heavy).Should().Be(3);
    }

    [Fact]
    public void NoGenuineTie_AllStrategiesProduceIdenticalResult()
    {
        // Không có 2 member nào cùng Remaining ở bất kỳ thời điểm nào trong quá trình giải →
        // strategy không có gì để chọn khác nhau, phải ra cùng 1 kết quả.
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var balances = new[] { new MemberBalance(a, 70), new MemberBalance(b, -30), new MemberBalance(c, -40) };

        var baseline = new GreedySettlementSolver().Solve(balances);
        var pastPair = new GreedySettlementSolver(SettlementTieBreakStrategy.PastPairAffinity, [(b, c)]).Solve(balances);
        var loadBalancing = new GreedySettlementSolver(SettlementTieBreakStrategy.LoadBalancing, Array.Empty<(Guid, Guid)>()).Solve(balances);

        pastPair.Should().BeEquivalentTo(baseline);
        loadBalancing.Should().BeEquivalentTo(baseline);
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
