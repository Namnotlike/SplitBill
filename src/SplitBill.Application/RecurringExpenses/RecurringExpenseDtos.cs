using SplitBill.Application.Expenses;

namespace SplitBill.Application.RecurringExpenses;

/// <summary>Mẫu khoản chi định kỳ (CLAUDE.md mục 15.7, bổ sung 2026-09-05).</summary>
public sealed record RecurringExpenseTemplateDto(
    Guid Id,
    Guid GroupId,
    string Title,
    long TotalAmount,
    long ExtraFeeAmount,
    string SplitMode,
    string Category,
    string? Note,
    string Interval,
    DateTimeOffset NextRunAt,
    bool IsActive,
    IReadOnlyList<ExpensePayerInput> Payers,
    DateTimeOffset CreatedAt);

public sealed record CreateRecurringExpenseRequest(
    string Title,
    long TotalAmount,
    long ExtraFeeAmount,
    IReadOnlyList<ExpensePayerInput> Payers,
    string SplitMode,
    SplitConfigInput SplitConfig,
    string Interval,
    DateTimeOffset FirstRunAt,
    string? Note = null,
    string? Category = null);
