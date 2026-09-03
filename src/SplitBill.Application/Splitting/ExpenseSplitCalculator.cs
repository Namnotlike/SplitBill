using SplitBill.Application.Common;
using SplitBill.Domain.Enums;

namespace SplitBill.Application.Splitting;

/// <summary>
/// Cài đặt CLAUDE.md mục 5.1: tính splits theo từng SplitMode, sau đó phân bổ ExtraFeeAmount
/// theo tỉ lệ phần gốc (bước 2 của pipeline). Class thuần, không phụ thuộc EF Core/DbContext.
/// </summary>
public sealed class ExpenseSplitCalculator : IExpenseSplitCalculator
{
    public ExpenseSplitResult Calculate(ExpenseSplitInput input)
    {
        var warnings = new List<Warning>();

        var baseSplits = input.SplitMode switch
        {
            SplitMode.Equal => CalculateEqual(input),
            SplitMode.Shares => CalculateShares(input),
            SplitMode.Percentage => CalculatePercentage(input, warnings),
            SplitMode.ExactAmount => CalculateExactAmount(input, warnings),
            SplitMode.Itemized => CalculateItemized(input),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input.SplitMode, "SplitMode không hợp lệ."),
        };

        var finalSplits = ApplyExtraFee(baseSplits, input.ExtraFeeAmount);

        var splits = finalSplits.Select(kv => new MemberAmount(kv.Key, kv.Value)).ToList();
        return new ExpenseSplitResult(splits, warnings);
    }

    private static Dictionary<Guid, long> CalculateEqual(ExpenseSplitInput input)
    {
        var memberIds = input.Config.MemberIds
            ?? throw new ArgumentException("SplitMode.Equal cần Config.MemberIds.", nameof(input));

        // Round-robin riêng cho Equal, thay largest remainder (CLAUDE.md mục 5.2).
        var allocation = RoundingAllocator.AllocateEqualRoundRobin(input.TotalAmount, memberIds, input.ExpenseId);
        return new Dictionary<Guid, long>(allocation);
    }

    private static Dictionary<Guid, long> CalculateShares(ExpenseSplitInput input)
    {
        var shares = input.Config.Shares
            ?? throw new ArgumentException("SplitMode.Shares cần Config.Shares.", nameof(input));

        var allocation = RoundingAllocator.AllocateLargestRemainder(input.TotalAmount, shares);
        return new Dictionary<Guid, long>(allocation);
    }

    private static Dictionary<Guid, long> CalculatePercentage(ExpenseSplitInput input, List<Warning> warnings)
    {
        var percentages = input.Config.Percentages
            ?? throw new ArgumentException("SplitMode.Percentage cần Config.Percentages.", nameof(input));

        var sumPercent = percentages.Values.Sum();
        if (sumPercent != 100m)
        {
            warnings.Add(new Warning(
                WarningCodes.PercentageSumNotHundred,
                $"Tổng phần trăm là {sumPercent}%, khác 100%."));
        }

        // amount_i = Total * p_i / 100, làm tròn ĐỘC LẬP từng người — mode này được loại trừ khỏi
        // bất biến Σ splits == TotalAmount (CLAUDE.md mục 5.2).
        var result = new Dictionary<Guid, long>();
        foreach (var (memberId, percent) in percentages)
        {
            var exact = input.TotalAmount * percent / 100m;
            result[memberId] = (long)Math.Round(exact, MidpointRounding.AwayFromZero);
        }

        return result;
    }

    private static Dictionary<Guid, long> CalculateExactAmount(ExpenseSplitInput input, List<Warning> warnings)
    {
        var exactAmounts = input.Config.ExactAmounts
            ?? throw new ArgumentException("SplitMode.ExactAmount cần Config.ExactAmounts.", nameof(input));

        var sum = exactAmounts.Values.Sum();
        if (sum != input.TotalAmount)
        {
            warnings.Add(new Warning(
                WarningCodes.ExactAmountSumMismatch,
                $"Tổng số tiền nhập tay là {sum}đ, khác TotalAmount {input.TotalAmount}đ."));
        }

        return new Dictionary<Guid, long>(exactAmounts);
    }

    private static Dictionary<Guid, long> CalculateItemized(ExpenseSplitInput input)
    {
        var items = input.Config.Items
            ?? throw new ArgumentException("SplitMode.Itemized cần Config.Items.", nameof(input));

        // Giả định: người gọi đảm bảo Σ item.Price == TotalAmount. Đây là điểm cần làm rõ thêm
        // trong đặc tả nếu 2 giá trị lệch nhau (chưa có quy tắc xử lý trong CLAUDE.md hiện tại).
        var totals = new Dictionary<Guid, long>();
        foreach (var item in items)
        {
            if (item.ConsumerMemberIds.Count == 0)
            {
                continue;
            }

            var weights = item.ConsumerMemberIds.Distinct().ToDictionary(id => id, _ => 1m);
            var allocation = RoundingAllocator.AllocateLargestRemainder(item.Price, weights);
            foreach (var (memberId, amount) in allocation)
            {
                totals[memberId] = totals.GetValueOrDefault(memberId) + amount;
            }
        }

        return totals;
    }

    private static Dictionary<Guid, long> ApplyExtraFee(Dictionary<Guid, long> baseSplits, long extraFeeAmount)
    {
        if (extraFeeAmount == 0 || baseSplits.Count == 0)
        {
            return baseSplits;
        }

        var sumBase = baseSplits.Values.Sum();

        IReadOnlyDictionary<Guid, long> feeAllocation;
        if (sumBase <= 0)
        {
            // Không ai có phần gốc dương (VD toàn ExactAmount = 0) → chia đều phụ phí bằng round-robin,
            // seed cố định để deterministic.
            feeAllocation = RoundingAllocator.AllocateEqualRoundRobin(extraFeeAmount, baseSplits.Keys.ToList(), Guid.Empty);
        }
        else
        {
            var weights = baseSplits.ToDictionary(kv => kv.Key, kv => (decimal)kv.Value);
            feeAllocation = RoundingAllocator.AllocateLargestRemainder(extraFeeAmount, weights);
        }

        var result = new Dictionary<Guid, long>(baseSplits);
        foreach (var (memberId, amount) in feeAllocation)
        {
            result[memberId] = result.GetValueOrDefault(memberId) + amount;
        }

        return result;
    }
}
