using SplitBill.Application.RecurringExpenses;

namespace SplitBill.Api.BackgroundJobs;

/// <summary>
/// Chạy nền, quét mẫu khoản chi định kỳ tới hạn mỗi <see cref="ScanInterval"/> (CLAUDE.md mục 15.7).
/// Bản thân class này chỉ lo vòng lặp/timer — toàn bộ logic nghiệp vụ (sinh Expense, tính NextRunAt kế
/// tiếp...) nằm ở <see cref="IRecurringExpenseRunner"/> (Application layer, test được độc lập không
/// cần chờ thời gian thật).
/// </summary>
public sealed class RecurringExpenseBackgroundService : BackgroundService
{
    // 1 giờ — đủ nhanh để 1 mẫu "hẹn giờ 00:00 hôm nay" không phải chờ quá lâu mới được xử lý, nhưng
    // không quét dồn dập gây tải DB không cần thiết (khoản chi định kỳ vốn chỉ đổi mỗi ngày/tuần/tháng).
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringExpenseBackgroundService> _logger;

    public RecurringExpenseBackgroundService(IServiceScopeFactory scopeFactory, ILogger<RecurringExpenseBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);

        // Quét ngay 1 lần lúc khởi động (không đợi hết chu kỳ đầu tiên) — để mẫu đã tới hạn từ trước
        // khi server khởi động lại không phải chờ thêm tới 1 giờ mới được xử lý.
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
            var runner = scope.ServiceProvider.GetRequiredService<IRecurringExpenseRunner>();
            var createdCount = await runner.RunDueTemplatesAsync(DateTimeOffset.UtcNow, stoppingToken);
            if (createdCount > 0)
            {
                _logger.LogInformation("Đã tự sinh {Count} khoản chi định kỳ.", createdCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Không để 1 lượt quét lỗi làm chết hẳn background service — lượt quét kế tiếp vẫn chạy.
            _logger.LogError(ex, "Lỗi khi quét khoản chi định kỳ.");
        }
    }
}
