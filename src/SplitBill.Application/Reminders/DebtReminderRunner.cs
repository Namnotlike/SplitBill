using Microsoft.Extensions.Logging;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Common;
using SplitBill.Application.Notifications;
using SettlementEntity = SplitBill.Domain.Entities.Settlement;

namespace SplitBill.Application.Reminders;

/// <summary>
/// Cài đặt CLAUDE.md mục 15.8. Phạm vi CỐ Ý thu hẹp về đúng 1 trường hợp có mốc thời gian rõ ràng:
/// Settlement còn Pending quá lâu (mốc = <see cref="SettlementEntity.CreatedAt"/>, hoặc lần nhắc gần nhất).
/// KHÔNG nhắc theo "số dư âm chung chung" của thành viên — số dư được cộng dồn từ nhiều Expense qua
/// nhiều thời điểm khác nhau, không có 1 mốc "bắt đầu nợ" duy nhất để tính "quá N ngày" một cách rõ
/// ràng và nhất quán, xem thêm ghi chú trong CLAUDE.md.
/// </summary>
public sealed class DebtReminderRunner : IDebtReminderRunner
{
    // 3 ngày — đủ để không làm phiền ngay lập tức (đôi khi người nhận chỉ chưa kịp mở app), nhưng
    // cũng không để 1 khoản Pending "trôi" quá lâu không ai nhắc.
    public static readonly TimeSpan ReminderInterval = TimeSpan.FromDays(3);

    private readonly ISettlementRepository _settlementRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly INotificationService _notificationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DebtReminderRunner> _logger;

    public DebtReminderRunner(
        ISettlementRepository settlementRepository,
        IGroupRepository groupRepository,
        INotificationService notificationService,
        IUnitOfWork unitOfWork,
        ILogger<DebtReminderRunner> logger)
    {
        _settlementRepository = settlementRepository;
        _groupRepository = groupRepository;
        _notificationService = notificationService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<int> RunDueRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var cutoff = asOf - ReminderInterval;
        var dueSettlements = await _settlementRepository.GetPendingDueForReminderAsync(cutoff, cancellationToken);
        var sentCount = 0;

        foreach (var settlement in dueSettlements)
        {
            try
            {
                var wasSent = await ProcessSettlementAsync(settlement, asOf, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                if (wasSent)
                {
                    sentCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhắc xác nhận settlement {SettlementId}", settlement.Id);
            }
        }

        return sentCount;
    }

    private async Task<bool> ProcessSettlementAsync(SettlementEntity settlement, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var group = await _groupRepository.GetByIdWithMembersAsync(settlement.GroupId, cancellationToken);
        if (group is null)
        {
            // Nhóm đã bị soft-delete — đánh dấu đã "xử lý" để không bị quét lại mỗi lượt, dù không có
            // gì để nhắc.
            settlement.LastReminderSentAt = asOf;
            return false;
        }

        var toMember = group.Members.FirstOrDefault(m => m.Id == settlement.ToMemberId);
        // Luôn cập nhật mốc — kể cả khi người nhận là khách vãng lai không có tài khoản để thông báo
        // (nếu không, settlement này sẽ bị coi là "chưa từng nhắc" mãi mãi và bị quét lại vô ích mỗi
        // lượt quét).
        settlement.LastReminderSentAt = asOf;

        if (toMember?.User is null)
        {
            return false;
        }

        var fromMember = group.Members.FirstOrDefault(m => m.Id == settlement.FromMemberId);
        var amountText = SupportedCurrencies.Format(settlement.Amount, group.Currency);
        await _notificationService.NotifyAsync(
            [new NotificationRecipient(toMember.User.Id, toMember.User.Email)],
            group.Id,
            "SettlementReminder",
            "Nhắc xác nhận thanh toán",
            $"{fromMember?.DisplayName ?? "Một thành viên"} đã báo chuyển {amountText} cho bạn trong nhóm \"{group.Name}\" nhưng chưa được xác nhận. Vui lòng kiểm tra và xác nhận/từ chối.",
            $"/Groups/SettlementPlan/{group.Id}",
            cancellationToken);

        return true;
    }
}
