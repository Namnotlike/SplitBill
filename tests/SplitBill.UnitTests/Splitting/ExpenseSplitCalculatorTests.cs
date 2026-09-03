using FluentAssertions;
using SplitBill.Application.Splitting;
using SplitBill.Domain.Enums;
using Xunit;

namespace SplitBill.UnitTests.Splitting;

/// <summary>Test cases bắt buộc ở CLAUDE.md mục 7.1 (làm tròn) + hành vi từng SplitMode ở mục 5.1.</summary>
public sealed class ExpenseSplitCalculatorTests
{
    private readonly ExpenseSplitCalculator _calculator = new();

    // R1: 100.000đ chia đều 3 người → 33.334 / 33.333 / 33.333, tổng = 100.000
    [Fact]
    public void R1_Equal_100000_By3_LargestRemainderSumsExactly()
    {
        var members = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var input = new ExpenseSplitInput(
            Guid.NewGuid(), 100_000, 0, SplitMode.Equal,
            new SplitConfig { MemberIds = members });

        var result = _calculator.Calculate(input);

        result.Splits.Sum(s => s.Amount).Should().Be(100_000);
        result.Splits.Select(s => s.Amount).Should().BeEquivalentTo(new long[] { 33_334, 33_333, 33_333 });
    }

    // R2: 10đ chia đều 3 người → 4 / 3 / 3
    [Fact]
    public void R2_Equal_10_By3()
    {
        var members = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var input = new ExpenseSplitInput(
            Guid.NewGuid(), 10, 0, SplitMode.Equal,
            new SplitConfig { MemberIds = members });

        var result = _calculator.Calculate(input);

        result.Splits.Sum(s => s.Amount).Should().Be(10);
        result.Splits.Select(s => s.Amount).Should().BeEquivalentTo(new long[] { 4, 3, 3 });
    }

    // R3: 1đ chia đều 5 người → 1 / 0 / 0 / 0 / 0
    [Fact]
    public void R3_Equal_1_By5()
    {
        var members = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var input = new ExpenseSplitInput(
            Guid.NewGuid(), 1, 0, SplitMode.Equal,
            new SplitConfig { MemberIds = members });

        var result = _calculator.Calculate(input);

        result.Splits.Sum(s => s.Amount).Should().Be(1);
        result.Splits.Select(s => s.Amount).Should().BeEquivalentTo(new long[] { 1, 0, 0, 0, 0 });
    }

    // R4: 100.000đ theo shares 1:1:2 → 25.000 / 25.000 / 50.000
    [Fact]
    public void R4_Shares_1_1_2()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var input = new ExpenseSplitInput(
            Guid.NewGuid(), 100_000, 0, SplitMode.Shares,
            new SplitConfig { Shares = new Dictionary<Guid, decimal> { [a] = 1, [b] = 1, [c] = 2 } });

        var result = _calculator.Calculate(input);

        result.Splits.Should().BeEquivalentTo(new[]
        {
            new MemberAmount(a, 25_000),
            new MemberAmount(b, 25_000),
            new MemberAmount(c, 50_000),
        });
    }

    // R5: 100.000đ chia đều 3 người (SplitMode.Equal), gọi 2 lần với ExpenseId khác nhau
    //     → người nhận phần dư phải khác nhau (CLAUDE.md mục 5.2 — round-robin CHỈ áp dụng cho Equal).
    [Fact]
    public void R5_Equal_RoundRobin_DiffersAcrossExpenseIds()
    {
        var members = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var recipientsWithExtra = new HashSet<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var input = new ExpenseSplitInput(
                Guid.NewGuid(), 100_000, 0, SplitMode.Equal,
                new SplitConfig { MemberIds = members });

            var result = _calculator.Calculate(input);
            var extraRecipient = result.Splits.Single(s => s.Amount == 33_334).MemberId;
            recipientsWithExtra.Add(extraRecipient);
        }

        // Với 12 lần gọi ExpenseId ngẫu nhiên khác nhau và 3 người, gần như chắc chắn xuất hiện
        // nhiều hơn 1 người nhận phần dư (không luôn rơi vào cùng một người).
        recipientsWithExtra.Should().HaveCountGreaterThan(1);
    }

    // Đối chứng: SplitMode.Shares với trọng số bằng nhau (1:1:1) KHÔNG round-robin theo ExpenseId —
    // luôn ra cùng một người nhận phần dư (tie-break cố định theo GroupMemberId ordinal).
    [Fact]
    public void Shares_EqualWeights_DoesNotRoundRobin_AcrossExpenseIds()
    {
        var members = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var weights = members.ToDictionary(m => m, _ => 1m);

        var recipientsWithExtra = new HashSet<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var input = new ExpenseSplitInput(
                Guid.NewGuid(), 100_000, 0, SplitMode.Shares,
                new SplitConfig { Shares = weights });

            var result = _calculator.Calculate(input);
            var extraRecipient = result.Splits.Single(s => s.Amount == 33_334).MemberId;
            recipientsWithExtra.Add(extraRecipient);
        }

        recipientsWithExtra.Should().HaveCount(1);
    }

    // R6: Total 300.000 + ExtraFee 30.000, chia đều 4 → tổng splits = 330.000
    [Fact]
    public void R6_Equal_WithExtraFee_SumsToTotalPlusFee()
    {
        var members = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var input = new ExpenseSplitInput(
            Guid.NewGuid(), 300_000, 30_000, SplitMode.Equal,
            new SplitConfig { MemberIds = members });

        var result = _calculator.Calculate(input);

        result.Splits.Sum(s => s.Amount).Should().Be(330_000);
    }

    [Fact]
    public void Percentage_SumNotHundred_StillSavesButWarns()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var input = new ExpenseSplitInput(
            Guid.NewGuid(), 100_000, 0, SplitMode.Percentage,
            new SplitConfig { Percentages = new Dictionary<Guid, decimal> { [a] = 40, [b] = 40 } });

        var result = _calculator.Calculate(input);

        result.Warnings.Should().ContainSingle(w => w.Code == Application.Common.WarningCodes.PercentageSumNotHundred);
        result.Splits.Sum(s => s.Amount).Should().Be(80_000);
    }

    [Fact]
    public void ExactAmount_SumMismatch_StillSavesButWarns()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var input = new ExpenseSplitInput(
            Guid.NewGuid(), 100_000, 0, SplitMode.ExactAmount,
            new SplitConfig { ExactAmounts = new Dictionary<Guid, long> { [a] = 40_000, [b] = 50_000 } });

        var result = _calculator.Calculate(input);

        result.Warnings.Should().ContainSingle(w => w.Code == Application.Common.WarningCodes.ExactAmountSumMismatch);
        result.Splits.Sum(s => s.Amount).Should().Be(90_000);
    }

    [Fact]
    public void Itemized_SplitsEachItemEquallyAmongAssignedConsumers()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var input = new ExpenseSplitInput(
            Guid.NewGuid(), 150_000, 0, SplitMode.Itemized,
            new SplitConfig
            {
                Items =
                [
                    new ItemizedLine("Lẩu", 100_000, [a, b]),
                    new ItemizedLine("Nước ngọt", 50_000, [a, b, c]),
                ],
            });

        var result = _calculator.Calculate(input);

        // Lẩu: 50.000/50.000 cho a,b. Nước ngọt: 16.667/16.667/16.666 (largest remainder).
        result.Splits.Sum(s => s.Amount).Should().Be(150_000);
        result.Splits.First(s => s.MemberId == c).Amount.Should().BeLessThan(20_000);
    }
}
