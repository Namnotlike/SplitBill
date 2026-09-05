namespace SplitBill.Application.RecurringExpenses;

/// <summary>
/// Quét mọi mẫu khoản chi định kỳ đã tới hạn và tự sinh Expense mới (CLAUDE.md mục 15.7). Tách khỏi
/// <see cref="IRecurringExpenseService"/> vì đây là hành động HỆ THỐNG tự động (chạy từ
/// background job), không có callerUserId như thao tác CRUD thông thường — và để test được mà không
/// cần chờ thời gian thật, nhận thẳng tham số <paramref name="asOf"/> làm "thời điểm hiện tại" thay vì
/// tự đọc <c>DateTimeOffset.UtcNow</c> bên trong.
/// </summary>
public interface IRecurringExpenseRunner
{
    /// <summary>Trả về số Expense mới đã được sinh ra trong lượt quét này.</summary>
    Task<int> RunDueTemplatesAsync(DateTimeOffset asOf, CancellationToken cancellationToken);
}
