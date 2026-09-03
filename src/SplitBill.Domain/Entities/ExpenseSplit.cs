namespace SplitBill.Domain.Entities;

/// <summary>Ai phải gánh bao nhiêu cho một khoản chi.</summary>
public sealed class ExpenseSplit
{
    public Guid Id { get; set; }
    public Guid ExpenseId { get; set; }
    public Guid GroupMemberId { get; set; }

    /// <summary>Có thể = 0 (người bị loại trừ khỏi khoản này).</summary>
    public long Amount { get; set; }

    public Expense? Expense { get; set; }
}
