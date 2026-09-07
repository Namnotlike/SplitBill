using System.Text.Json;
using Microsoft.Extensions.Logging;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Notifications;
using SplitBill.Application.Splitting;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;

namespace SplitBill.Application.RecurringExpenses;

/// <summary>Cài đặt CLAUDE.md mục 15.7. Xem docstring <see cref="IRecurringExpenseRunner"/>.</summary>
public sealed class RecurringExpenseRunner : IRecurringExpenseRunner
{
    private readonly IRecurringExpenseRepository _recurringExpenseRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IExpenseSplitCalculator _splitCalculator;
    private readonly INotificationService _notificationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RecurringExpenseRunner> _logger;

    public RecurringExpenseRunner(
        IRecurringExpenseRepository recurringExpenseRepository,
        IGroupRepository groupRepository,
        IExpenseRepository expenseRepository,
        IAuditLogRepository auditLogRepository,
        IExpenseSplitCalculator splitCalculator,
        INotificationService notificationService,
        IUnitOfWork unitOfWork,
        ILogger<RecurringExpenseRunner> logger)
    {
        _recurringExpenseRepository = recurringExpenseRepository;
        _groupRepository = groupRepository;
        _expenseRepository = expenseRepository;
        _auditLogRepository = auditLogRepository;
        _splitCalculator = splitCalculator;
        _notificationService = notificationService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<int> RunDueTemplatesAsync(DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var dueTemplates = await _recurringExpenseRepository.GetDueTemplatesAsync(asOf, cancellationToken);
        var createdCount = 0;

        foreach (var template in dueTemplates)
        {
            try
            {
                var created = await ProcessTemplateAsync(template, asOf, cancellationToken);
                // Lưu riêng từng mẫu (không gộp 1 SaveChanges cho cả lượt quét) — 1 mẫu lỗi (vd nhóm đã
                // bị xóa) không được kéo theo mất tiến độ của các mẫu khác đã xử lý xong trong cùng lượt.
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                if (created)
                {
                    createdCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi sinh khoản chi định kỳ từ mẫu {TemplateId} (nhóm {GroupId})", template.Id, template.GroupId);
            }
        }

        return createdCount;
    }

    /// <returns>true nếu đã sinh được 1 Expense mới; false nếu mẫu bị tắt thay vì sinh khoản chi
    /// (nhóm đã xóa, hoặc mẫu tham chiếu thành viên không còn active — xem ghi chú bên dưới).</returns>
    private async Task<bool> ProcessTemplateAsync(RecurringExpenseTemplate template, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var group = await _groupRepository.GetByIdWithMembersAsync(template.GroupId, cancellationToken);
        if (group is null)
        {
            // Nhóm đã bị soft-delete sau khi mẫu được tạo — tắt mẫu luôn thay vì cứ lỗi lặp lại mỗi
            // lượt quét.
            template.IsActive = false;
            return false;
        }

        var payers = JsonSerializer.Deserialize<List<ExpensePayerInput>>(template.PayersJson) ?? [];
        var splitConfigInput = string.IsNullOrWhiteSpace(template.SplitConfigJson)
            ? new SplitConfigInput()
            : JsonSerializer.Deserialize<SplitConfigInput>(template.SplitConfigJson) ?? new SplitConfigInput();

        // ⚠️ Bảo mật/nghiệp vụ (phát hiện qua security-review 2026-09-07, quyết định người dùng —
        // "tắt mẫu + báo nhóm"): các GroupMemberId đóng băng trong PayersJson/SplitConfigJson lúc tạo
        // mẫu có thể không còn active tại thời điểm chạy (thành viên đã rời nhóm — chỉ rời được khi
        // net == 0 lúc rời, nhưng điều đó không ngăn mẫu định kỳ tiếp tục gán tiền cho họ ở lần chạy
        // sau). Một khi rời nhóm, họ không còn thấy được /groups/{id}/balances (yêu cầu caller đang
        // active) nên không có cách nào biết hay tranh chấp khoản nợ "ma" này. Thay vì âm thầm sinh
        // Expense gán tiền cho người đã rời nhóm, tắt hẳn mẫu và báo cho các thành viên active còn lại
        // biết để họ chủ động tạo lại mẫu (loại bỏ người đã rời) nếu vẫn muốn dùng tiếp.
        var activeMemberIds = group.Members.Where(m => m.IsActive).Select(m => m.Id).ToHashSet();
        var referencedMemberIds = payers.Select(p => p.MemberId)
            .Concat(splitConfigInput.MemberIds ?? [])
            .Concat(splitConfigInput.Shares?.Select(s => s.MemberId) ?? [])
            .Concat(splitConfigInput.Percentages?.Select(p => p.MemberId) ?? [])
            .Concat(splitConfigInput.ExactAmounts?.Select(e => e.MemberId) ?? [])
            .Concat(splitConfigInput.Items?.SelectMany(i => i.ConsumerMemberIds) ?? [])
            .Distinct();

        if (referencedMemberIds.Any(id => !activeMemberIds.Contains(id)))
        {
            template.IsActive = false;
            _logger.LogWarning(
                "Mẫu khoản chi định kỳ {TemplateId} (nhóm {GroupId}) đã bị tự động tắt vì tham chiếu thành viên không còn active trong nhóm.",
                template.Id, template.GroupId);

            var deactivationRecipients = group.Members
                .Where(m => m.IsActive && m.User is not null)
                .Select(m => new NotificationRecipient(m.User!.Id, m.User.Email))
                .ToList();
            await _notificationService.NotifyAsync(
                deactivationRecipients,
                group.Id,
                "RecurringTemplateDeactivated",
                "Khoản chi định kỳ đã bị tắt",
                $"Mẫu khoản chi định kỳ \"{template.Title}\" trong nhóm \"{group.Name}\" đã bị tự động tắt vì có thành viên trong mẫu không còn ở trong nhóm. Vui lòng tạo lại mẫu nếu vẫn muốn dùng tiếp.",
                $"/Groups/RecurringExpenses/{group.Id}",
                cancellationToken);
            return false;
        }

        var input = new ExpenseSplitInput(Guid.NewGuid(), template.TotalAmount, template.ExtraFeeAmount, template.SplitMode, BuildSplitConfig(splitConfigInput));
        var result = _splitCalculator.Calculate(input);

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            Title = template.Title,
            TotalAmount = template.TotalAmount,
            ExtraFeeAmount = template.ExtraFeeAmount,
            SplitMode = template.SplitMode,
            Category = template.Category,
            SplitConfigJson = template.SplitConfigJson,
            Note = template.Note,
            // Ngày phát sinh = đúng lịch đã hẹn (NextRunAt), KHÔNG phải thời điểm job thực sự chạy —
            // job có thể trễ vài phút/giờ so với lịch, nhưng khoản chi vẫn nên ghi đúng ngày nó "đến hạn".
            OccurredAt = template.NextRunAt,
            CreatedByMemberId = template.CreatedByMemberId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var payer in payers)
        {
            expense.Payers.Add(new ExpensePayer { Id = Guid.NewGuid(), ExpenseId = expense.Id, GroupMemberId = payer.MemberId, Amount = payer.Amount });
        }
        foreach (var split in result.Splits)
        {
            expense.Splits.Add(new ExpenseSplit { Id = Guid.NewGuid(), ExpenseId = expense.Id, GroupMemberId = split.MemberId, Amount = split.Amount });
        }

        await _expenseRepository.AddAsync(expense, cancellationToken);
        // Dựng ExpenseDto đầy đủ cho AfterJson — CHỨ KHÔNG để null — vì BuildSummary (mục 15.5) parse
        // AfterJson thành ExpenseDto để hiển thị "đã thêm khoản chi X (số tiền)" trên Timeline; AfterJson
        // rỗng sẽ làm khoản chi tự sinh từ mẫu định kỳ hiện dòng Timeline cụt lủn "đã thêm 1 khoản chi"
        // (không có tên/số tiền), mất hết giá trị so với khoản chi tạo tay.
        var afterDto = new ExpenseDto(
            expense.Id, expense.GroupId, expense.Title, expense.TotalAmount, expense.ExtraFeeAmount,
            expense.SplitMode.ToString(), expense.Note, expense.ReceiptImageUrl, expense.OccurredAt,
            expense.Payers.Select(p => new ExpenseMemberAmountDto(p.GroupMemberId, p.Amount)).ToList(),
            expense.Splits.Select(s => new ExpenseMemberAmountDto(s.GroupMemberId, s.Amount)).ToList(),
            RowVersion: string.Empty, expense.SplitConfigJson, expense.Category.ToString());
        await _auditLogRepository.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            EntityType = "Expense",
            EntityId = expense.Id,
            Action = "Created",
            ActorMemberId = template.CreatedByMemberId,
            AfterJson = JsonSerializer.Serialize(afterDto),
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);

        // NextRunAt nhảy tới lần kế tiếp CÒN Ở TƯƠNG LAI so với asOf — nếu job từng bị gián đoạn dài
        // ngày (server tắt vài tuần), KHÔNG bù lại từng kỳ đã lỡ (tránh sinh hàng loạt khoản chi dồn
        // cục), chỉ sinh đúng 1 khoản cho lượt quét này rồi nhảy thẳng tới kỳ hợp lệ tiếp theo.
        var next = template.NextRunAt;
        do
        {
            next = Advance(next, template.Interval);
        }
        while (next <= asOf);
        template.NextRunAt = next;

        var recipients = group.Members
            .Where(m => m.IsActive && m.User is not null && m.Id != template.CreatedByMemberId)
            .Select(m => new NotificationRecipient(m.User!.Id, m.User.Email))
            .ToList();
        await _notificationService.NotifyAsync(
            recipients,
            group.Id,
            "ExpenseCreated",
            "Khoản chi định kỳ mới",
            $"Khoản chi định kỳ \"{expense.Title}\" ({expense.TotalAmount:N0}đ) vừa được tự động thêm vào nhóm \"{group.Name}\".",
            $"/Expenses/Index/{group.Id}",
            cancellationToken);

        return true;
    }

    private static DateTimeOffset Advance(DateTimeOffset current, RecurrenceInterval interval) => interval switch
    {
        RecurrenceInterval.Daily => current.AddDays(1),
        RecurrenceInterval.Weekly => current.AddDays(7),
        RecurrenceInterval.Monthly => current.AddMonths(1),
        _ => current.AddMonths(1),
    };

    private static SplitConfig BuildSplitConfig(SplitConfigInput input) => new()
    {
        MemberIds = input.MemberIds,
        Shares = input.Shares?.ToDictionary(s => s.MemberId, s => s.Weight),
        Percentages = input.Percentages?.ToDictionary(p => p.MemberId, p => p.Percent),
        ExactAmounts = input.ExactAmounts?.ToDictionary(e => e.MemberId, e => e.Amount),
        Items = input.Items?.Select(i => new ItemizedLine(i.Name, i.Price, i.ConsumerMemberIds)).ToList(),
    };
}
