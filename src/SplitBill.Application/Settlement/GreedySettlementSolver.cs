namespace SplitBill.Application.Settlement;

/// <summary>
/// Cài đặt CLAUDE.md mục 6.2. Đảm bảo tối đa n-1 giao dịch, độ phức tạp O(n^2 log n) (n nhỏ trong
/// thực tế; re-sort mỗi vòng lặp để giữ đúng "lớn nhất còn lại" sau mỗi lần khớp một phần).
/// </summary>
public sealed class GreedySettlementSolver : ISettlementSolver
{
    public IReadOnlyList<SettlementTransaction> Solve(IReadOnlyList<MemberBalance> balances)
    {
        var debtors = balances.Where(b => b.Net < 0)
            .Select(b => new Entry(b.MemberId, -b.Net))
            .ToList();
        var creditors = balances.Where(b => b.Net > 0)
            .Select(b => new Entry(b.MemberId, b.Net))
            .ToList();

        var transactions = new List<SettlementTransaction>();

        while (debtors.Count > 0 && creditors.Count > 0)
        {
            SortDescending(debtors);
            SortDescending(creditors);

            var debtor = debtors[0];
            var creditor = creditors[0];

            var amount = Math.Min(debtor.Remaining, creditor.Remaining);
            transactions.Add(new SettlementTransaction(debtor.MemberId, creditor.MemberId, amount));

            debtor.Remaining -= amount;
            creditor.Remaining -= amount;

            if (debtor.Remaining == 0)
            {
                debtors.RemoveAt(0);
            }

            if (creditor.Remaining == 0)
            {
                creditors.RemoveAt(0);
            }
        }

        return transactions;
    }

    // Sắp xếp giảm dần theo Remaining; tie-break: GroupMemberId ordinal tăng dần (CLAUDE.md mục 6.2).
    private static void SortDescending(List<Entry> entries) => entries.Sort((a, b) =>
    {
        var byAmount = b.Remaining.CompareTo(a.Remaining);
        return byAmount != 0 ? byAmount : string.CompareOrdinal(a.MemberId.ToString(), b.MemberId.ToString());
    });

    private sealed class Entry
    {
        public Entry(Guid memberId, long remaining)
        {
            MemberId = memberId;
            Remaining = remaining;
        }

        public Guid MemberId { get; }
        public long Remaining { get; set; }
    }
}
