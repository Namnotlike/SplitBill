using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

/// <summary>CLAUDE.md mục 15.7 — Khoản chi định kỳ.</summary>
public interface IRecurringExpenseRepository
{
    Task<RecurringExpenseTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<List<RecurringExpenseTemplate>> GetByGroupIdAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>Mọi mẫu còn <see cref="RecurringExpenseTemplate.IsActive"/> có
    /// <see cref="RecurringExpenseTemplate.NextRunAt"/> &lt;= <paramref name="asOf"/> — dùng bởi
    /// <c>RecurringExpenseRunner</c> để quét các mẫu đã tới hạn sinh khoản chi mới.</summary>
    Task<List<RecurringExpenseTemplate>> GetDueTemplatesAsync(DateTimeOffset asOf, CancellationToken cancellationToken);

    Task AddAsync(RecurringExpenseTemplate template, CancellationToken cancellationToken);
}
