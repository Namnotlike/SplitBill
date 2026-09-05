namespace SplitBill.Application.Reminders;

/// <summary>
/// Nhắc nợ tự động (CLAUDE.md mục 15.8) — quét mọi <c>Settlement</c> còn <c>Pending</c> quá lâu và
/// nhắc người nhận (ToMember) xác nhận/từ chối. Nhận <paramref name="asOf"/> làm "thời điểm hiện tại"
/// thay vì tự đọc <c>DateTimeOffset.UtcNow</c> — cùng lý do với <c>IRecurringExpenseRunner</c> (mục
/// 15.7): test được mà không cần chờ thời gian thật.
/// </summary>
public interface IDebtReminderRunner
{
    /// <summary>Trả về số lượt nhắc đã gửi trong lần quét này.</summary>
    Task<int> RunDueRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken);
}
