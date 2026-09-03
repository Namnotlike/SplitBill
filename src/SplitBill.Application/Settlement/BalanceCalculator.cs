using SplitBill.Application.Splitting;
using SplitBill.Domain.Enums;

namespace SplitBill.Application.Settlement;

/// <summary>
/// Cài đặt CLAUDE.md mục 6.1. Class thuần, không phụ thuộc EF Core/DbContext — gọi từ tầng
/// Infrastructure sau khi đã nạp dữ liệu thô từ DB.
/// </summary>
public sealed class BalanceCalculator : IBalanceCalculator
{
    public IReadOnlyList<MemberBalance> Calculate(
        IEnumerable<ExpenseBalanceInput> expenses,
        IEnumerable<SettlementBalanceInput> settlements)
    {
        var net = new Dictionary<Guid, long>();

        void Add(Guid memberId, long amount) => net[memberId] = net.GetValueOrDefault(memberId) + amount;

        foreach (var expense in expenses)
        {
            foreach (var payer in expense.Payers)
            {
                Add(payer.MemberId, payer.Amount);
            }

            foreach (var split in expense.Splits)
            {
                Add(split.MemberId, -split.Amount);
            }

            ApplyMismatchDelta(expense, Add);
        }

        foreach (var settlement in settlements)
        {
            // Settlement Pending/Rejected không tính vào balance (CLAUDE.md mục 6.1).
            if (settlement.Status != SettlementStatus.Confirmed)
            {
                continue;
            }

            // FromMemberId trả nợ → net tiến về 0 từ phía âm → cộng.
            // ToMemberId được trả → net tiến về 0 từ phía dương → trừ.
            // (Xem cảnh báo sửa lỗi dấu ở CLAUDE.md mục 6.1.)
            Add(settlement.FromMemberId, settlement.Amount);
            Add(settlement.ToMemberId, -settlement.Amount);
        }

        return net.Select(kv => new MemberBalance(kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Xử lý lệch tổng Σpayers ≠ Σsplits (CLAUDE.md mục 5.3): phần delta được cộng vào split ẢO
    /// của (các) người ứng tiền, chia theo tỉ lệ số tiền họ đã ứng, KHÔNG ghi đè ExpenseSplit trong DB.
    /// </summary>
    private static void ApplyMismatchDelta(ExpenseBalanceInput expense, Action<Guid, long> add)
    {
        if (expense.Payers.Count == 0)
        {
            return;
        }

        var sumPayers = expense.Payers.Sum(p => p.Amount);
        var sumSplits = expense.Splits.Sum(s => s.Amount);
        var delta = sumPayers - sumSplits;
        if (delta == 0)
        {
            return;
        }

        // Chỉ payer có Amount > 0 mới nhận trọng số (Amount payer luôn > 0 theo validation 5.4,
        // nhưng phòng hờ dữ liệu cũ/không hợp lệ).
        var weights = expense.Payers
            .Where(p => p.Amount > 0)
            .ToDictionary(p => p.MemberId, p => (decimal)p.Amount);

        if (weights.Count == 0)
        {
            return;
        }

        var allocation = RoundingAllocator.AllocateLargestRemainder(delta, weights);
        foreach (var (memberId, amount) in allocation)
        {
            // Delta cộng vào phần "phải chịu" ảo của payer → trừ vào net giống một split bình thường.
            add(memberId, -amount);
        }
    }
}
