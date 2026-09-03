namespace SplitBill.Domain.Entities;

/// <summary>Ai đã móc tiền ra ứng trước cho một khoản chi.</summary>
public sealed class ExpensePayer
{
    public Guid Id { get; set; }
    public Guid ExpenseId { get; set; }
    public Guid GroupMemberId { get; set; }

    /// <summary>Phải &gt; 0.</summary>
    public long Amount { get; set; }

    public Expense? Expense { get; set; }
}
