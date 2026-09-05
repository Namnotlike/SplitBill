using SplitBill.Application.Reminders;

namespace SplitBill.Api.BackgroundJobs;

/// <summary>
/// Chạy nền, quét Settlement Pending quá lâu mỗi <see cref="ScanInterval"/> (CLAUDE.md mục 15.8). Cùng
/// mẫu với <see cref="RecurringExpenseBackgroundService"/> — chỉ lo vòng lặp timer, toàn bộ logic nằm
/// ở <see cref="IDebtReminderRunner"/> (Application layer).
/// </summary>
public sealed class DebtReminderBackgroundService : BackgroundService
{
    // Nhắc nợ có hạt mịn theo ngày (DebtReminderRunner.ReminderInterval = 3 ngày), quét mỗi 6 giờ là
    // đủ nhanh để không lệch quá nửa ngày so với đúng mốc N ngày, không cần quét dày như khoản chi
    // định kỳ (vốn có thể có chu kỳ Daily, cần quét sát giờ hơn).
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DebtReminderBackgroundService> _logger;

    public DebtReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<DebtReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        await ScanOnceAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ScanOnceAsync(stoppingToken);
        }
    }

    private async Task ScanOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IDebtReminderRunner>();
            var sentCount = await runner.RunDueRemindersAsync(DateTimeOffset.UtcNow, stoppingToken);
            if (sentCount > 0)
            {
                _logger.LogInformation("Đã gửi {Count} lượt nhắc xác nhận thanh toán.", sentCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lỗi khi quét nhắc nợ.");
        }
    }
}
