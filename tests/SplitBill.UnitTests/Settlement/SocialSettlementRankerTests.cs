using FluentAssertions;
using SplitBill.Application.Settlement;
using Xunit;

namespace SplitBill.UnitTests.Settlement;

/// <summary>Test cho <see cref="SocialSettlementRanker"/> — CLAUDE.md mục 6.4, chấm điểm thuần túy
/// trên các danh sách giao dịch dựng sẵn (không phụ thuộc solver nào cả).</summary>
public sealed class SocialSettlementRankerTests
{
    private readonly SocialSettlementRanker _ranker = new();
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();
    private readonly Guid _d = Guid.NewGuid();

    [Fact]
    public void SelectBest_NoCandidates_ReturnsEmpty()
    {
        _ranker.SelectBest([], []).Should().BeEmpty();
    }

    [Fact]
    public void SelectBest_SingleCandidate_ReturnsItUnchanged()
    {
        var plan = new[] { new SettlementTransaction(_a, _b, 12_345) };

        _ranker.SelectBest([plan], []).Should().BeEquivalentTo(plan);
    }

    [Fact]
    public void SelectBest_IgnoresCandidatesWithMoreTransactionsThanMinimum()
    {
        var minimal = new IReadOnlyList<SettlementTransaction>[]
        {
            [new SettlementTransaction(_a, _b, 100)],
        }[0];
        var larger = new SettlementTransaction[] { new(_a, _c, 40), new(_b, _c, 60) };

        var result = _ranker.SelectBest([larger, minimal], []);

        result.Should().BeEquivalentTo(minimal);
    }

    [Fact]
    public void SelectBest_PrefersCandidateWithMorePastPairMatches()
    {
        // Cùng 2 giao dịch, cùng tổng tiền — chỉ khác AI trả cho AI.
        var noMatch = new SettlementTransaction[] { new(_a, _c, 100), new(_b, _d, 100) };
        var oneMatch = new SettlementTransaction[] { new(_a, _b, 100), new(_c, _d, 100) };
        var pastPairs = new[] { (_a, _b) };

        var result = _ranker.SelectBest([noMatch, oneMatch], pastPairs);

        result.Should().BeEquivalentTo(oneMatch);
    }

    [Fact]
    public void SelectBest_MatchesPastPair_RegardlessOfDirection()
    {
        // pastPairs lưu (b, a) — giao dịch thực tế lại là a→b (chiều ngược) — vẫn phải tính là khớp
        // vì mục 6.4 nói "đã từng có Settlement với nhau" (không phân biệt chiều).
        var candidate = new SettlementTransaction[] { new(_a, _b, 100) };
        var other = new SettlementTransaction[] { new(_c, _d, 100) };
        var pastPairs = new[] { (_b, _a) };

        var result = _ranker.SelectBest([other, candidate], pastPairs);

        result.Should().BeEquivalentTo(candidate);
    }

    [Fact]
    public void SelectBest_WhenPastPairMatchesTie_PrefersFewerOver3OutgoingViolations()
    {
        // Cả 2 phương án đều 0 điểm past-pair — khác nhau ở chỗ 1 phương án dồn 4 giao dịch lên
        // cùng 1 người (vi phạm ngưỡng 3), phương án kia rải đều.
        var concentrated = new SettlementTransaction[]
        {
            new(_a, _b, 10), new(_a, _c, 10), new(_a, _d, 10), new(_a, Guid.NewGuid(), 10),
        };
        var spread = new SettlementTransaction[]
        {
            new(_a, _b, 10), new(_b, _c, 10), new(_c, _d, 10), new(_d, _a, 10),
        };

        var result = _ranker.SelectBest([concentrated, spread], []);

        result.Should().BeEquivalentTo(spread);
    }

    [Fact]
    public void SelectBest_WhenEarlierCriteriaTie_PrefersMoreRoundAmounts()
    {
        var lessRound = new SettlementTransaction[] { new(_a, _b, 12_345), new(_c, _d, 1_000) };
        var moreRound = new SettlementTransaction[] { new(_a, _b, 5_000), new(_c, _d, 1_000) };

        var result = _ranker.SelectBest([lessRound, moreRound], []);

        result.Should().BeEquivalentTo(moreRound);
    }
}
