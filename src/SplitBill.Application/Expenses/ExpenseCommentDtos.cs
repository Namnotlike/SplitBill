namespace SplitBill.Application.Expenses;

/// <summary>Bình luận trên khoản chi (CLAUDE.md mục 19).</summary>
public sealed record ExpenseCommentDto(
    Guid Id,
    Guid ExpenseId,
    Guid AuthorMemberId,
    string AuthorDisplayName,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record CreateExpenseCommentRequest(string Content);
