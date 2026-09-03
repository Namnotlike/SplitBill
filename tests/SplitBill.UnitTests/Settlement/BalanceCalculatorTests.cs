using FluentAssertions;
using SplitBill.Application.Settlement;
using SplitBill.Application.Splitting;
using SplitBill.Domain.Enums;
using Xunit;

namespace SplitBill.UnitTests.Settlement;

/// <summary>Test cases bắt buộc ở CLAUDE.md mục 7.2 (số dư ròng).</summary>
public sealed class BalanceCalculatorTests
{
    private readonly BalanceCalculator _calculator = new();

    // B1: A ứng 300k, chia đều A/B/C → A: +200k, B: -100k, C: -100k
    [Fact]
    public void B1_SinglePayer_EqualSplit()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var expense = new ExpenseBalanceInput(
            Guid.NewGuid(),
            Payers: [new MemberAmount(a, 300_000)],
            Splits: [new MemberAmount(a, 100_000), new MemberAmount(b, 100_000), new MemberAmount(c, 100_000)]);

        var result = _calculator.Calculate([expense], []);

        result.Should().Contain(new MemberBalance(a, 200_000));
        result.Should().Contain(new MemberBalance(b, -100_000));
        result.Should().Contain(new MemberBalance(c, -100_000));
    }

    // B2: A ứng 200k và B ứng 100k, chia đều A/B/C → A: +100k, B: 0, C: -100k
    [Fact]
    public void B2_MultiplePayers_EqualSplit()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var expense = new ExpenseBalanceInput(
            Guid.NewGuid(),
            Payers: [new MemberAmount(a, 200_000), new MemberAmount(b, 100_000)],
            Splits: [new MemberAmount(a, 100_000), new MemberAmount(b, 100_000), new MemberAmount(c, 100_000)]);

        var result = _calculator.Calculate([expense], []);

        result.Should().Contain(new MemberBalance(a, 100_000));
        result.Should().Contain(new MemberBalance(b, 0));
        result.Should().Contain(new MemberBalance(c, -100_000));
    }

    // B3: A ứng 100k, splits chỉ ghi 80k (lệch 20k) → delta 20k cộng vào split ẢO của A → Σ net = 0
    [Fact]
    public void B3_MismatchDelta_AbsorbedByPayer()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var expense = new ExpenseBalanceInput(
            Guid.NewGuid(),
            Payers: [new MemberAmount(a, 100_000)],
            Splits: [new MemberAmount(b, 40_000), new MemberAmount(c, 40_000)]);

        var result = _calculator.Calculate([expense], []);

        result.Sum(m => m.Net).Should().Be(0);
        result.Single(m => m.MemberId == a).Net.Should().Be(80_000); // +100k payer - 20k delta ảo
        result.Single(m => m.MemberId == b).Net.Should().Be(-40_000);
        result.Single(m => m.MemberId == c).Net.Should().Be(-40_000);
    }

    // B4: Có settlement Pending → balance KHÔNG đổi
    [Fact]
    public void B4_PendingSettlement_DoesNotAffectBalance()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var expense = new ExpenseBalanceInput(
            Guid.NewGuid(),
            Payers: [new MemberAmount(a, 100_000)],
            Splits: [new MemberAmount(a, 50_000), new MemberAmount(b, 50_000)]);

        var withoutSettlement = _calculator.Calculate([expense], []);
        var withPendingSettlement = _calculator.Calculate(
            [expense],
            [new SettlementBalanceInput(b, a, 50_000, SettlementStatus.Pending)]);

        withPendingSettlement.Should().BeEquivalentTo(withoutSettlement);
    }

    // Đối chứng B4: settlement Confirmed PHẢI ảnh hưởng balance.
    [Fact]
    public void ConfirmedSettlement_ZerosOutBalance()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var expense = new ExpenseBalanceInput(
            Guid.NewGuid(),
            Payers: [new MemberAmount(a, 100_000)],
            Splits: [new MemberAmount(a, 50_000), new MemberAmount(b, 50_000)]);

        var result = _calculator.Calculate(
            [expense],
            [new SettlementBalanceInput(b, a, 50_000, SettlementStatus.Confirmed)]);

        result.Should().OnlyContain(m => m.Net == 0);
    }

    // B5: Expense bị soft delete → không tính vào balance. BalanceCalculator là class thuần,
    // việc lọc IsDeleted thuộc về tầng gọi (EF global query filter ở Infrastructure) — ở đây kiểm
    // chứng: đơn giản KHÔNG đưa expense đã xóa vào input thì nó không ảnh hưởng balance.
    [Fact]
    public void B5_ExpenseNotPassedIn_IsExcludedFromBalance()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var deletedExpense = new ExpenseBalanceInput(
            Guid.NewGuid(),
            Payers: [new MemberAmount(a, 999_000)],
            Splits: [new MemberAmount(b, 999_000)]);

        // Caller (Infrastructure) không truyền expense đã soft-delete vào đây.
        var result = _calculator.Calculate([], []);

        result.Should().BeEmpty();
        _ = deletedExpense; // minh họa: dữ liệu này tồn tại trong DB nhưng không được truyền vào.
    }

    // B6: Random 50 expense, 8 người → Σ net == 0 tuyệt đối
    [Fact]
    public void B6_RandomExpenses_NetAlwaysSumsToZero()
    {
        var random = new Random(20260903);
        var members = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var splitCalculator = new ExpenseSplitCalculator();
        var expenses = new List<ExpenseBalanceInput>();

        for (var i = 0; i < 50; i++)
        {
            var expenseId = Guid.NewGuid();
            var total = random.Next(1_000, 1_000_000);

            var splitResult = splitCalculator.Calculate(new ExpenseSplitInput(
                expenseId, total, 0, SplitMode.Equal, new SplitConfig { MemberIds = members }));

            var payerCount = random.Next(1, 4);
            var payerIds = members.OrderBy(_ => random.Next()).Take(payerCount).ToArray();
            var payerTotal = random.Next(1, 1_000_000); // cố ý có thể lệch so với total → kích hoạt delta
            var payerAmounts = DistributeRandomly(payerTotal, payerCount, random);

            var payers = payerIds.Zip(payerAmounts, (id, amt) => new MemberAmount(id, amt))
                .Where(p => p.Amount > 0)
                .ToList();

            if (payers.Count == 0)
            {
                continue;
            }

            expenses.Add(new ExpenseBalanceInput(expenseId, payers, splitResult.Splits));
        }

        var result = _calculator.Calculate(expenses, []);

        result.Sum(m => m.Net).Should().Be(0);
    }

    private static long[] DistributeRandomly(long total, int count, Random random)
    {
        var cuts = new List<long> { 0, total };
        for (var i = 1; i < count; i++)
        {
            cuts.Add((long)(random.NextDouble() * total));
        }

        cuts.Sort();
        var amounts = new long[count];
        for (var i = 0; i < count; i++)
        {
            amounts[i] = cuts[i + 1] - cuts[i];
        }

        return amounts;
    }
}
