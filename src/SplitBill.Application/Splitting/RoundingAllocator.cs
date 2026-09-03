namespace SplitBill.Application.Splitting;

/// <summary>
/// Cài đặt luật làm tròn bắt buộc ở CLAUDE.md mục 5.2: largest remainder method với tie-break
/// theo GroupMemberId ordinal, và round-robin riêng cho SplitMode.Equal.
/// </summary>
public static class RoundingAllocator
{
    /// <summary>
    /// Phân bổ <paramref name="total"/> theo trọng số bằng largest remainder method.
    /// Đảm bảo Σ kết quả == total tuyệt đối.
    /// </summary>
    public static IReadOnlyDictionary<Guid, long> AllocateLargestRemainder(long total, IReadOnlyDictionary<Guid, decimal> weights)
    {
        if (weights.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        var sumWeight = weights.Values.Sum();
        if (sumWeight <= 0)
        {
            throw new ArgumentException("Tổng trọng số phải lớn hơn 0.", nameof(weights));
        }

        var exactShare = new Dictionary<Guid, decimal>();
        var baseAmount = new Dictionary<Guid, long>();
        var sumBase = 0L;

        foreach (var (memberId, weight) in weights)
        {
            var share = total * weight / sumWeight;
            exactShare[memberId] = share;
            var floor = (long)Math.Floor(share);
            baseAmount[memberId] = floor;
            sumBase += floor;
        }

        var remainder = (int)(total - sumBase);

        // Phần dư thập phân giảm dần; tie-break: GroupMemberId.ToString() ordinal, nhỏ hơn đứng trước.
        var ordered = weights.Keys
            .OrderByDescending(id => exactShare[id] - baseAmount[id])
            .ThenBy(id => id.ToString(), StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < remainder && i < ordered.Count; i++)
        {
            baseAmount[ordered[i]] += 1;
        }

        return baseAmount;
    }

    /// <summary>
    /// Chia đều <paramref name="total"/> cho <paramref name="memberIds"/>, phần dư xoay vòng bắt đầu
    /// từ <c>|expenseId.GetHashCode()| % n</c> để không luôn rơi vào cùng một người qua nhiều hóa đơn.
    /// Đảm bảo Σ kết quả == total tuyệt đối.
    /// </summary>
    public static IReadOnlyDictionary<Guid, long> AllocateEqualRoundRobin(long total, IReadOnlyList<Guid> memberIds, Guid expenseId)
    {
        if (memberIds.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        // Sắp xếp ordinal để kết quả deterministic (CLAUDE.md mục 5.2).
        var ordered = memberIds.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList();
        var n = ordered.Count;
        var baseShare = total / n;
        var remainder = (int)(total % n);

        var result = ordered.ToDictionary(id => id, _ => baseShare);

        if (remainder > 0)
        {
            var startIndex = (int)((uint)expenseId.GetHashCode() % (uint)n);
            for (var i = 0; i < remainder; i++)
            {
                var idx = (startIndex + i) % n;
                result[ordered[idx]] += 1;
            }
        }

        return result;
    }
}
