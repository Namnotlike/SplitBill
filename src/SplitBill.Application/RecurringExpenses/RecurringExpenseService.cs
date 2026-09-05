using System.Text.Json;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses;
using SplitBill.Application.Splitting;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.RecurringExpenses;

/// <summary>Cài đặt CLAUDE.md mục 15.7 — quản lý mẫu khoản chi định kỳ (tạo/xem/tắt). Việc SINH khoản
/// chi mới từ mẫu tới hạn do <see cref="IRecurringExpenseRunner"/> đảm nhiệm riêng — tách biệt vì đó
/// là hành động hệ thống tự động, không có "người gọi" (callerUserId) như các thao tác ở đây.</summary>
public sealed class RecurringExpenseService : IRecurringExpenseService
{
    private readonly IRecurringExpenseRepository _recurringExpenseRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IExpenseSplitCalculator _splitCalculator;
    private readonly IUnitOfWork _unitOfWork;

    public RecurringExpenseService(
        IRecurringExpenseRepository recurringExpenseRepository,
        IGroupRepository groupRepository,
        IExpenseSplitCalculator splitCalculator,
        IUnitOfWork unitOfWork)
    {
        _recurringExpenseRepository = recurringExpenseRepository;
        _groupRepository = groupRepository;
        _splitCalculator = splitCalculator;
        _unitOfWork = unitOfWork;
    }

    public async Task<RecurringExpenseTemplateDto> CreateAsync(Guid callerUserId, Guid groupId, CreateRecurringExpenseRequest request, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (group.Type != GroupType.Recurring)
        {
            throw new DomainException(ErrorCodes.GroupTypeNotRecurring, "Chỉ nhóm loại \"Dùng lại nhiều lần\" mới tạo được khoản chi định kỳ.");
        }

        if (!Enum.TryParse<RecurrenceInterval>(request.Interval, ignoreCase: true, out var interval))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Interval '{request.Interval}' không hợp lệ.");
        }

        ValidatePayers(group, request.Payers);

        if (!Enum.TryParse<SplitMode>(request.SplitMode, ignoreCase: true, out var splitMode))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"SplitMode '{request.SplitMode}' không hợp lệ.");
        }

        // Chạy thử pipeline tính split NGAY LÚC TẠO mẫu (không lưu kết quả) — chỉ để báo lỗi sớm nếu
        // SplitConfig sai (vd MemberId không thuộc nhóm), thay vì âm thầm lỗi ở lần chạy tự động đầu
        // tiên (có thể vài ngày/tuần sau, khi không còn ai đang theo dõi để sửa kịp).
        var previewInput = new ExpenseSplitInput(Guid.NewGuid(), request.TotalAmount, request.ExtraFeeAmount, splitMode, BuildSplitConfig(request.SplitConfig));
        var previewResult = _splitCalculator.Calculate(previewInput);
        ValidateMembersBelongToGroup(group, previewResult.Splits.Select(s => s.MemberId));

        var template = new RecurringExpenseTemplate
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            Title = request.Title,
            TotalAmount = request.TotalAmount,
            ExtraFeeAmount = request.ExtraFeeAmount,
            SplitMode = splitMode,
            SplitConfigJson = JsonSerializer.Serialize(request.SplitConfig),
            PayersJson = JsonSerializer.Serialize(request.Payers),
            Category = ExpenseCategoryParser.Parse(request.Category),
            Note = request.Note,
            Interval = interval,
            NextRunAt = request.FirstRunAt,
            IsActive = true,
            CreatedByMemberId = caller.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _recurringExpenseRepository.AddAsync(template, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(template);
    }

    public async Task<IReadOnlyList<RecurringExpenseTemplateDto>> GetByGroupIdAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var templates = await _recurringExpenseRepository.GetByGroupIdAsync(groupId, cancellationToken);
        return templates.OrderByDescending(t => t.CreatedAt).Select(ToDto).ToList();
    }

    public async Task DeactivateAsync(Guid callerUserId, Guid templateId, CancellationToken cancellationToken)
    {
        var template = await _recurringExpenseRepository.GetByIdAsync(templateId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.RecurringExpenseNotFound, "Không tìm thấy mẫu khoản chi định kỳ.");
        var group = await LoadGroupAsync(template.GroupId, cancellationToken);
        ResolveCallerMember(group, callerUserId); // mọi thành viên đều tắt được, giống quyền tạo/sửa Expense

        template.IsActive = false;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

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

    private static SplitConfig BuildSplitConfig(SplitConfigInput input) => new()
    {
        MemberIds = input.MemberIds,
        Shares = input.Shares?.ToDictionary(s => s.MemberId, s => s.Weight),
        Percentages = input.Percentages?.ToDictionary(p => p.MemberId, p => p.Percent),
        ExactAmounts = input.ExactAmounts?.ToDictionary(e => e.MemberId, e => e.Amount),
        Items = input.Items?.Select(i => new ItemizedLine(i.Name, i.Price, i.ConsumerMemberIds)).ToList(),
    };

    private async Task<Group> LoadGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await _groupRepository.GetByIdWithMembersAsync(groupId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Không tìm thấy nhóm.");

    private static GroupMember ResolveCallerMember(Group group, Guid callerUserId) =>
        group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive)
            ?? throw new DomainException(ErrorCodes.MemberNotInGroup, "Bạn không phải thành viên của nhóm này.");

    private static RecurringExpenseTemplateDto ToDto(RecurringExpenseTemplate template) => new(
        template.Id,
        template.GroupId,
        template.Title,
        template.TotalAmount,
        template.ExtraFeeAmount,
        template.SplitMode.ToString(),
        template.Category.ToString(),
        template.Note,
        template.Interval.ToString(),
        template.NextRunAt,
        template.IsActive,
        (JsonSerializer.Deserialize<List<ExpensePayerInput>>(template.PayersJson) ?? []),
        template.CreatedAt);
}
