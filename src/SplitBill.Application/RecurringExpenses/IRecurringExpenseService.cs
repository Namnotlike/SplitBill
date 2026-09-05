namespace SplitBill.Application.RecurringExpenses;

/// <summary>CLAUDE.md mục 15.7 — Khoản chi định kỳ, chỉ áp dụng cho nhóm loại Recurring.</summary>
public interface IRecurringExpenseService
{
    Task<RecurringExpenseTemplateDto> CreateAsync(Guid callerUserId, Guid groupId, CreateRecurringExpenseRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecurringExpenseTemplateDto>> GetByGroupIdAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Tắt mẫu (IsActive = false) — không xóa, giữ lịch sử các khoản chi đã sinh ra.</summary>
    Task DeactivateAsync(Guid callerUserId, Guid templateId, CancellationToken cancellationToken);
}
