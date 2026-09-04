namespace SplitBill.Application.Settlement;

/// <summary>
/// Chiến lược chọn giữa các entry đang HÒA điểm (cùng Remaining lớn nhất) khi ghép giao dịch tiếp
/// theo. Dùng để sinh nhiều phương án ứng viên cho ràng buộc mềm (CLAUDE.md mục 6.4) — mỗi entry
/// hòa điểm đại diện cho những người/khoản nợ có thể hoán đổi cho nhau mà KHÔNG đổi tổng số giao
/// dịch (do cùng Remaining nên kết quả đối xứng), chỉ đổi CỤ THỂ ai chuyển cho ai.
/// </summary>
public enum SettlementTieBreakStrategy
{
    /// <summary>Mặc định — GroupMemberId ordinal tăng dần (CLAUDE.md mục 6.2), hành vi gốc không đổi.</summary>
    OrdinalAscending,

    /// <summary>Trong số các entry đang hòa, ưu tiên người đã từng có Settlement với entry phía đối
    /// diện (tiêu chí 1, mục 6.4).</summary>
    PastPairAffinity,

    /// <summary>Trong số các debtor đang hòa, ưu tiên người có ÍT giao dịch chuyển đi hơn trong
    /// phương án đang xây dựng (tiêu chí 2, mục 6.4 — tránh 1 người phải chuyển quá 3 lượt).</summary>
    LoadBalancing,
}

/// <summary>
/// Cài đặt CLAUDE.md mục 6.2. Đảm bảo tối đa n-1 giao dịch, độ phức tạp O(n^2 log n) (n nhỏ trong
/// thực tế; re-sort mỗi vòng lặp để giữ đúng "lớn nhất còn lại" sau mỗi lần khớp một phần).
/// </summary>
public sealed class GreedySettlementSolver : ISettlementSolver
{
    private readonly SettlementTieBreakStrategy _tieBreakStrategy;
    private readonly IReadOnlyCollection<(Guid MemberA, Guid MemberB)> _pastPairs;

    public GreedySettlementSolver() : this(SettlementTieBreakStrategy.OrdinalAscending, Array.Empty<(Guid, Guid)>())
    {
    }

    /// <param name="tieBreakStrategy">Xem <see cref="SettlementTieBreakStrategy"/>.</param>
    /// <param name="pastPairs">Các cặp thành viên đã từng có Settlement với nhau trong nhóm — chỉ cần
    /// khi <paramref name="tieBreakStrategy"/> là <see cref="SettlementTieBreakStrategy.PastPairAffinity"/>.</param>
    public GreedySettlementSolver(
        SettlementTieBreakStrategy tieBreakStrategy,
        IReadOnlyCollection<(Guid MemberA, Guid MemberB)> pastPairs)
    {
        _tieBreakStrategy = tieBreakStrategy;
        _pastPairs = pastPairs;
    }

    public IReadOnlyList<SettlementTransaction> Solve(IReadOnlyList<MemberBalance> balances)
    {
        var debtors = balances.Where(b => b.Net < 0)
            .Select(b => new Entry(b.MemberId, -b.Net))
            .ToList();
        var creditors = balances.Where(b => b.Net > 0)
            .Select(b => new Entry(b.MemberId, b.Net))
            .ToList();
        var outgoingCount = new Dictionary<Guid, int>();

        var transactions = new List<SettlementTransaction>();

        while (debtors.Count > 0 && creditors.Count > 0)
        {
            SortDescending(debtors);
            SortDescending(creditors);

            var debtor = PickDebtor(debtors, creditors, outgoingCount);
            var creditor = PickCreditor(creditors, debtor);

            var amount = Math.Min(debtor.Remaining, creditor.Remaining);
            transactions.Add(new SettlementTransaction(debtor.MemberId, creditor.MemberId, amount));
            outgoingCount[debtor.MemberId] = outgoingCount.GetValueOrDefault(debtor.MemberId) + 1;

            debtor.Remaining -= amount;
            creditor.Remaining -= amount;

            if (debtor.Remaining == 0)
            {
                debtors.Remove(debtor);
            }

            if (creditor.Remaining == 0)
            {
                creditors.Remove(creditor);
            }
        }

        return transactions;
    }

    // Chọn debtor để ghép giao dịch tiếp theo. Nhóm "đang hòa" (cùng Remaining lớn nhất, đã sort ở
    // trên) có thể hoán đổi cho nhau mà không đổi tổng số giao dịch — OrdinalAscending luôn chọn
    // phần tử đầu (hành vi gốc); các strategy còn lại re-chọn trong đúng nhóm hòa đó theo tiêu chí
    // xã hội tương ứng, KHÔNG bao giờ chọn ra ngoài nhóm hòa nên không phá vỡ tính tối ưu của Greedy.
    private Entry PickDebtor(List<Entry> debtors, List<Entry> creditors, Dictionary<Guid, int> outgoingCount)
    {
        var tieGroup = TopTiedGroup(debtors);
        if (tieGroup.Count == 1)
        {
            return tieGroup[0];
        }

        return _tieBreakStrategy switch
        {
            SettlementTieBreakStrategy.PastPairAffinity =>
                tieGroup.FirstOrDefault(d => HasPastPairWithAny(d.MemberId, TopTiedGroup(creditors))) ?? tieGroup[0],
            SettlementTieBreakStrategy.LoadBalancing =>
                tieGroup
                    .OrderBy(d => outgoingCount.GetValueOrDefault(d.MemberId))
                    .ThenBy(d => d.MemberId.ToString(), StringComparer.Ordinal)
                    .First(),
            _ => tieGroup[0],
        };
    }

    private Entry PickCreditor(List<Entry> creditors, Entry chosenDebtor)
    {
        var tieGroup = TopTiedGroup(creditors);
        if (tieGroup.Count == 1)
        {
            return tieGroup[0];
        }

        return _tieBreakStrategy switch
        {
            SettlementTieBreakStrategy.PastPairAffinity =>
                tieGroup.FirstOrDefault(c => HasPastPair(chosenDebtor.MemberId, c.MemberId)) ?? tieGroup[0],
            _ => tieGroup[0],
        };
    }

    private bool HasPastPairWithAny(Guid memberId, List<Entry> candidates) =>
        candidates.Any(c => HasPastPair(memberId, c.MemberId));

    private bool HasPastPair(Guid a, Guid b) =>
        _pastPairs.Any(p => (p.MemberA == a && p.MemberB == b) || (p.MemberA == b && p.MemberB == a));

    private static List<Entry> TopTiedGroup(List<Entry> sortedEntries)
    {
        var top = sortedEntries[0].Remaining;
        return sortedEntries.TakeWhile(e => e.Remaining == top).ToList();
    }

    // Sắp xếp giảm dần theo Remaining; tie-break: GroupMemberId ordinal tăng dần (CLAUDE.md mục 6.2).
    // Đây là base ordering CHUNG cho mọi strategy — PickDebtor/PickCreditor ở trên chỉ re-chọn phần tử
    // đại diện trong đúng nhóm đang hòa (Remaining bằng nhau), không phá vỡ tính deterministic của
    // phần còn lại trong danh sách.
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
