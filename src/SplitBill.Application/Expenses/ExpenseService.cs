using System.Text.Json;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Common;
using SplitBill.Application.Notifications;
using SplitBill.Application.Splitting;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Expenses;

/// <summary>Cài đặt CRUD khoản chi (CLAUDE.md mục 5, 8).</summary>
public sealed class ExpenseService : IExpenseService
{
    private readonly IExpenseRepository _expenseRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IExpenseSplitCalculator _splitCalculator;
    private readonly IReceiptImageRepository _receiptImageRepository;
    private readonly INotificationService _notificationService;

    public ExpenseService(
        IExpenseRepository expenseRepository,
        IGroupRepository groupRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IExpenseSplitCalculator splitCalculator,
        IReceiptImageRepository receiptImageRepository,
        INotificationService notificationService)
    {
        _expenseRepository = expenseRepository;
        _groupRepository = groupRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _splitCalculator = splitCalculator;
        _receiptImageRepository = receiptImageRepository;
        _notificationService = notificationService;
    }

    public async Task<PagedResult<ExpenseDto>> GetPagedAsync(Guid callerUserId, Guid groupId, int page, int pageSize, ExpenseFilter filter, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var (items, total) = await _expenseRepository.GetPagedAsync(groupId, page, pageSize, filter, cancellationToken);
        return new PagedResult<ExpenseDto>(items.Select(ToDto).ToList(), page, pageSize, total);
    }

    public async Task<IReadOnlyList<ExpenseDto>> GetAllForExportAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var expenses = await _expenseRepository.GetAllByGroupIdAsync(groupId, cancellationToken);
        return expenses.OrderByDescending(e => e.OccurredAt).Select(ToDto).ToList();
    }

    public async Task<ExpenseDto> GetByIdAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken)
    {
        var expense = await LoadExpenseAsync(expenseId, cancellationToken);
        var group = await LoadGroupAsync(expense.GroupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        return ToDto(expense);
    }

    public async Task<ExpenseResult> CreateAsync(Guid callerUserId, Guid groupId, CreateExpenseRequest request, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        var (splitMode, splits, warnings) = ComputeSplits(group, Guid.NewGuid(), request.TotalAmount, request.ExtraFeeAmount, request.SplitMode, request.SplitConfig);

        ValidatePayers(group, request.Payers);

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            Title = request.Title,
            TotalAmount = request.TotalAmount,
            ExtraFeeAmount = request.ExtraFeeAmount,
            SplitMode = splitMode,
            Category = ParseCategory(request.Category),
            SplitConfigJson = JsonSerializer.Serialize(request.SplitConfig),
            Note = request.Note,
            ReceiptImageUrl = request.ReceiptImageUrl,
            OccurredAt = request.OccurredAt,
            CreatedByMemberId = caller.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var payer in request.Payers)
        {
            expense.Payers.Add(new ExpensePayer { Id = Guid.NewGuid(), ExpenseId = expense.Id, GroupMemberId = payer.MemberId, Amount = payer.Amount });
        }

        foreach (var split in splits)
        {
            expense.Splits.Add(new ExpenseSplit { Id = Guid.NewGuid(), ExpenseId = expense.Id, GroupMemberId = split.MemberId, Amount = split.Amount });
        }

        warnings.AddRange(ComputeMismatchWarning(request.Payers, splits));

        await _expenseRepository.AddAsync(expense, cancellationToken);
        await WriteAuditLogAsync(group.Id, expense.Id, "Created", caller.Id, null, ToDto(expense), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Thông báo "khoản chi mới" (CLAUDE.md mục 13) — mọi thành viên khác có tài khoản, trừ người tạo.
        var recipients = group.Members
            .Where(m => m.IsActive && m.User is not null && m.Id != caller.Id)
            .Select(m => new NotificationRecipient(m.User!.Id, m.User.Email))
            .ToList();
        await _notificationService.NotifyAsync(
            recipients,
            group.Id,
            "ExpenseCreated",
            "Khoản chi mới",
            $"{caller.DisplayName} vừa thêm khoản chi \"{expense.Title}\" ({expense.TotalAmount:N0}đ) trong nhóm \"{group.Name}\".",
            $"/Expenses/Index/{group.Id}",
            cancellationToken);

        return new ExpenseResult(ToDto(expense), warnings);
    }

    public async Task<ExpenseResult> UpdateAsync(Guid callerUserId, Guid expenseId, UpdateExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = await LoadExpenseAsync(expenseId, cancellationToken);
        var group = await LoadGroupAsync(expense.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (Convert.ToBase64String(expense.RowVersion) != request.RowVersion)
        {
            throw new DomainException(ErrorCodes.ConcurrencyConflict, "Dữ liệu đã bị người khác thay đổi. Vui lòng tải lại và thử lại.");
        }

        var before = ToDto(expense);

        var (splitMode, splits, warnings) = ComputeSplits(group, expense.Id, request.TotalAmount, request.ExtraFeeAmount, request.SplitMode, request.SplitConfig);
        ValidatePayers(group, request.Payers);

        expense.Title = request.Title;
        expense.TotalAmount = request.TotalAmount;
        expense.ExtraFeeAmount = request.ExtraFeeAmount;
        expense.SplitMode = splitMode;
        expense.Category = ParseCategory(request.Category);
        expense.SplitConfigJson = JsonSerializer.Serialize(request.SplitConfig);
        expense.Note = request.Note;
        expense.ReceiptImageUrl = request.ReceiptImageUrl;
        expense.OccurredAt = request.OccurredAt;
        expense.UpdatedAt = DateTimeOffset.UtcNow;

        // Xóa + thêm tường minh qua DbSet (không Clear()/Add() qua navigation) — xem ghi chú ở
        // IExpenseRepository.RemovePayers/AddPayers. Dựa vào graph-tracking từ navigation khi gán
        // lại cả collection từng gây "entity does not exist in the store" (EF nhầm insert thành update).
        _expenseRepository.RemovePayers(expense.Payers.ToList());
        var newPayers = request.Payers
            .Select(p => new ExpensePayer { Id = Guid.NewGuid(), ExpenseId = expense.Id, GroupMemberId = p.MemberId, Amount = p.Amount })
            .ToList();
        _expenseRepository.AddPayers(newPayers);
        expense.Payers = newPayers;

        _expenseRepository.RemoveSplits(expense.Splits.ToList());
        var newSplits = splits
            .Select(s => new ExpenseSplit { Id = Guid.NewGuid(), ExpenseId = expense.Id, GroupMemberId = s.MemberId, Amount = s.Amount })
            .ToList();
        _expenseRepository.AddSplits(newSplits);
        expense.Splits = newSplits;

        warnings.AddRange(ComputeMismatchWarning(request.Payers, splits));

        await WriteAuditLogAsync(group.Id, expense.Id, "Updated", caller.Id, before, ToDto(expense), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ExpenseResult(ToDto(expense), warnings);
    }

    public async Task DeleteAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken)
    {
        var expense = await LoadExpenseAsync(expenseId, cancellationToken);
        var group = await LoadGroupAsync(expense.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        // Chụp lại trạng thái TRƯỚC khi xóa (CLAUDE.md mục 15.5) — trước đây AuditLog của hành động
        // Deleted luôn ghi before=null, khiến không thể biết khoản chi bị xóa tên gì/bao nhiêu tiền
        // khi xem lại lịch sử (audit log "mù" ở đúng sự kiện quan trọng nhất). Timeline hoạt động nhóm
        // cần before này để hiển thị "đã xóa khoản chi X".
        var before = ToDto(expense);

        expense.IsDeleted = true;
        expense.UpdatedAt = DateTimeOffset.UtcNow;

        await WriteAuditLogAsync(group.Id, expense.Id, "Deleted", caller.Id, before, null, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExpenseDto> UploadReceiptImageAsync(Guid callerUserId, Guid expenseId, Stream content, string fileName, string contentType, CancellationToken cancellationToken)
    {
        var expense = await LoadExpenseAsync(expenseId, cancellationToken);
        var group = await LoadGroupAsync(expense.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        await _receiptImageRepository.UpsertAsync(new ReceiptImage
        {
            Id = Guid.NewGuid(),
            ExpenseId = expenseId,
            Content = bytes,
            ContentType = contentType,
            FileName = fileName,
            SizeBytes = bytes.LongLength,
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);

        var before = ToDto(expense);
        // URL nội bộ trỏ tới endpoint GET có kiểm tra quyền — ảnh thật lưu trong bảng ReceiptImage
        // (SQL Server), không phải đường dẫn ổ đĩa (CLAUDE.md mục 4.1, quyết định người dùng 2026-09-03).
        expense.ReceiptImageUrl = $"/api/v1/expenses/{expenseId}/receipt-image";
        expense.UpdatedAt = DateTimeOffset.UtcNow;

        await WriteAuditLogAsync(group.Id, expense.Id, "Updated", caller.Id, before, ToDto(expense), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(expense);
    }

    public async Task<ReceiptImageContentDto> GetReceiptImageAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken)
    {
        var expense = await LoadExpenseAsync(expenseId, cancellationToken);
        var group = await LoadGroupAsync(expense.GroupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var image = await _receiptImageRepository.GetByExpenseIdAsync(expenseId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ExpenseNotFound, "Khoản chi chưa có ảnh hóa đơn.");

        return new ReceiptImageContentDto(image.Content, image.ContentType, image.FileName);
    }

    public PreviewSplitResult PreviewSplit(PreviewSplitRequest request)
    {
        if (!Enum.TryParse<SplitMode>(request.SplitMode, ignoreCase: true, out var splitMode))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"SplitMode '{request.SplitMode}' không hợp lệ.");
        }

        var config = BuildSplitConfig(request.SplitConfig);
        var input = new ExpenseSplitInput(Guid.NewGuid(), request.TotalAmount, request.ExtraFeeAmount, splitMode, config);
        var result = _splitCalculator.Calculate(input);

        return new PreviewSplitResult(
            result.Splits.Select(s => new ExpenseMemberAmountDto(s.MemberId, s.Amount)).ToList(),
            result.Warnings);
    }

    /// <summary>CLAUDE.md mục 15.3 — null/rỗng mặc định Other; giá trị không hợp lệ báo lỗi rõ ràng
    /// thay vì âm thầm rơi về Other (tránh người dùng tưởng đã gán nhãn nhưng thực ra gõ sai tên).</summary>
    private static ExpenseCategory ParseCategory(string? categoryText)
    {
        if (string.IsNullOrWhiteSpace(categoryText))
        {
            return ExpenseCategory.Other;
        }

        if (!Enum.TryParse<ExpenseCategory>(categoryText, ignoreCase: true, out var category))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Category '{categoryText}' không hợp lệ.");
        }

        return category;
    }

    private (SplitMode Mode, IReadOnlyList<MemberAmount> Splits, List<Warning> Warnings) ComputeSplits(
        Group group, Guid expenseId, long totalAmount, long extraFeeAmount, string splitModeText, SplitConfigInput configInput)
    {
        if (!Enum.TryParse<SplitMode>(splitModeText, ignoreCase: true, out var splitMode))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"SplitMode '{splitModeText}' không hợp lệ.");
        }

        var config = BuildSplitConfig(configInput);
        var input = new ExpenseSplitInput(expenseId, totalAmount, extraFeeAmount, splitMode, config);
        var result = _splitCalculator.Calculate(input);

        ValidateMembersBelongToGroup(group, result.Splits.Select(s => s.MemberId));

        return (splitMode, result.Splits, result.Warnings.ToList());
    }

    private static SplitConfig BuildSplitConfig(SplitConfigInput input) => new()
    {
        MemberIds = input.MemberIds,
        Shares = input.Shares?.ToDictionary(s => s.MemberId, s => s.Weight),
        Percentages = input.Percentages?.ToDictionary(p => p.MemberId, p => p.Percent),
        ExactAmounts = input.ExactAmounts?.ToDictionary(e => e.MemberId, e => e.Amount),
        Items = input.Items?.Select(i => new ItemizedLine(i.Name, i.Price, i.ConsumerMemberIds)).ToList(),
    };

    private static void ValidatePayers(Group group, IReadOnlyList<ExpensePayerInput> payers)
    {
        if (payers.Count == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Payers không được rỗng.");
        }

        if (payers.Any(p => p.Amount <= 0))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Amount của mỗi payer phải > 0.");
        }

        if (payers.Select(p => p.MemberId).Distinct().Count() != payers.Count)
        {
            throw new DomainException(ErrorCodes.DuplicateMemberId, "Payers không được trùng GroupMemberId.");
        }

        ValidateMembersBelongToGroup(group, payers.Select(p => p.MemberId));
    }

    private static void ValidateMembersBelongToGroup(Group group, IEnumerable<Guid> memberIds)
    {
        var groupMemberIds = group.Members.Select(m => m.Id).ToHashSet();
        foreach (var memberId in memberIds.Distinct())
        {
            if (!groupMemberIds.Contains(memberId))
            {
                throw new DomainException(ErrorCodes.MemberNotInGroup, $"GroupMemberId {memberId} không thuộc nhóm này.");
            }
        }
    }

    private static IEnumerable<Warning> ComputeMismatchWarning(IReadOnlyList<ExpensePayerInput> payers, IReadOnlyList<MemberAmount> splits)
    {
        var sumPayers = payers.Sum(p => p.Amount);
        var sumSplits = splits.Sum(s => s.Amount);
        var delta = sumPayers - sumSplits;
        if (delta != 0)
        {
            yield return new Warning(
                WarningCodes.SplitTotalMismatch,
                $"Lệch {Math.Abs(delta)}đ so với tổng hóa đơn — phần chênh do người ứng tiền chịu.",
                delta);
        }
    }

    private async Task<Group> LoadGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await _groupRepository.GetByIdWithMembersAsync(groupId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Không tìm thấy nhóm.");

    private async Task<Expense> LoadExpenseAsync(Guid expenseId, CancellationToken cancellationToken) =>
        await _expenseRepository.GetByIdAsync(expenseId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ExpenseNotFound, "Không tìm thấy khoản chi.");

    private static GroupMember ResolveCallerMember(Group group, Guid callerUserId) =>
        group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive)
            ?? throw new DomainException(ErrorCodes.MemberNotInGroup, "Bạn không phải thành viên của nhóm này.");

    private async Task WriteAuditLogAsync(
        Guid groupId, Guid expenseId, string action, Guid actorMemberId,
        object? before, object? after, CancellationToken cancellationToken)
    {
        await _auditLogRepository.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            EntityType = "Expense",
            EntityId = expenseId,
            Action = action,
            ActorMemberId = actorMemberId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }

    private static ExpenseDto ToDto(Expense expense) => new(
        expense.Id,
        expense.GroupId,
        expense.Title,
        expense.TotalAmount,
        expense.ExtraFeeAmount,
        expense.SplitMode.ToString(),
        expense.Note,
        expense.ReceiptImageUrl,
        expense.OccurredAt,
        expense.Payers.Select(p => new ExpenseMemberAmountDto(p.GroupMemberId, p.Amount)).ToList(),
        expense.Splits.Select(s => new ExpenseMemberAmountDto(s.GroupMemberId, s.Amount)).ToList(),
        Convert.ToBase64String(expense.RowVersion),
        expense.SplitConfigJson,
        expense.Category.ToString());
}
