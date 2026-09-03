namespace SplitBill.Application.Splitting;

/// <summary>Một dòng item trong chế độ chia theo món (SplitMode.Itemized).</summary>
public sealed record ItemizedLine(string Name, long Price, IReadOnlyList<Guid> ConsumerMemberIds);

/// <summary>
/// Input gốc của người dùng cho một khoản chi, tùy theo <c>SplitMode</c> chỉ nhóm trường tương ứng
/// được điền (xem bảng ở CLAUDE.md mục 5.1). Đây chính là nội dung được serialize vào
/// <c>Expense.SplitConfigJson</c>.
/// </summary>
public sealed class SplitConfig
{
    /// <summary>Dùng cho SplitMode.Equal: danh sách người tham gia chia đều.</summary>
    public IReadOnlyList<Guid>? MemberIds { get; init; }

    /// <summary>Dùng cho SplitMode.Shares: memberId → trọng số (phải &gt; 0).</summary>
    public IReadOnlyDictionary<Guid, decimal>? Shares { get; init; }

    /// <summary>Dùng cho SplitMode.Percentage: memberId → phần trăm.</summary>
    public IReadOnlyDictionary<Guid, decimal>? Percentages { get; init; }

    /// <summary>Dùng cho SplitMode.ExactAmount: memberId → số tiền nhập tay.</summary>
    public IReadOnlyDictionary<Guid, long>? ExactAmounts { get; init; }

    /// <summary>Dùng cho SplitMode.Itemized: danh sách món ăn kèm người ăn.</summary>
    public IReadOnlyList<ItemizedLine>? Items { get; init; }
}

/// <summary>Input đầy đủ cho <see cref="IExpenseSplitCalculator"/> (CLAUDE.md mục 5.1).</summary>
public sealed record ExpenseSplitInput(
    Guid ExpenseId,
    long TotalAmount,
    long ExtraFeeAmount,
    Domain.Enums.SplitMode SplitMode,
    SplitConfig Config);
