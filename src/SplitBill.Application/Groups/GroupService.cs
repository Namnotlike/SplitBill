using System.Text.Json;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses; // PagedResult<T>, ExpenseDto
using SplitBill.Application.Notifications;
using SplitBill.Application.Settlement;
using SplitBill.Application.Settlements; // SettlementDto — dựng Summary cho timeline (mục 15.5)
using SplitBill.Application.Splitting;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Groups;

/// <summary>Cài đặt CRUD nhóm/thành viên + phân quyền Owner/Member (CLAUDE.md mục 4.4, 8).</summary>
public sealed class GroupService : IGroupService
{
    private readonly IGroupRepository _groupRepository;
    private readonly IUserRepository _userRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IShareTokenGenerator _shareTokenGenerator;
    private readonly IBalanceCalculator _balanceCalculator;
    private readonly INotificationService _notificationService;

    public GroupService(
        IGroupRepository groupRepository,
        IUserRepository userRepository,
        IExpenseRepository expenseRepository,
        ISettlementRepository settlementRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IShareTokenGenerator shareTokenGenerator,
        IBalanceCalculator balanceCalculator,
        INotificationService notificationService)
    {
        _groupRepository = groupRepository;
        _userRepository = userRepository;
        _expenseRepository = expenseRepository;
        _settlementRepository = settlementRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _shareTokenGenerator = shareTokenGenerator;
        _balanceCalculator = balanceCalculator;
        _notificationService = notificationService;
    }

    public async Task<GroupDto> CreateAsync(Guid callerUserId, CreateGroupRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<GroupType>(request.Type, ignoreCase: true, out var groupType))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"GroupType '{request.Type}' không hợp lệ.");
        }

        string shareToken;
        do
        {
            shareToken = _shareTokenGenerator.Generate();
        }
        while (await _groupRepository.ShareTokenExistsAsync(shareToken, cancellationToken));

        var creator = await _userRepository.GetByIdAsync(callerUserId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.InvalidCredentials, "Tài khoản không tồn tại.");

        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Type = groupType,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? SupportedCurrencies.Default : request.Currency,
            CreatedByUserId = callerUserId,
            ShareToken = shareToken,
            SimplifyDebts = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var ownerMember = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = callerUserId,
            DisplayName = creator.DisplayName, // snapshot tên tại thời điểm tạo nhóm (CLAUDE.md mục 4.1)
            Role = GroupMemberRole.Owner,
            IsActive = true,
            JoinedAt = DateTimeOffset.UtcNow,
        };
        group.Members.Add(ownerMember);

        await _groupRepository.AddAsync(group, cancellationToken);
        await WriteAuditLogAsync(group.Id, "Group", group.Id, "Created", ownerMember.Id, null, ToDto(group), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(group);
    }

    public async Task<IReadOnlyList<GroupSummaryDto>> GetMyGroupsAsync(Guid callerUserId, CancellationToken cancellationToken)
    {
        var groups = await _groupRepository.GetByUserIdAsync(callerUserId, cancellationToken);
        return groups.Select(g => new GroupSummaryDto(g.Id, g.Name, g.Type.ToString(), g.IsArchived, g.Currency)).ToList();
    }

    public async Task<GroupDto> GetByIdAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId); // chỉ thành viên mới được xem chi tiết
        return ToDto(group);
    }

    public async Task<GroupDto> UpdateAsync(Guid callerUserId, Guid groupId, UpdateGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);
        RequireOwner(caller);

        var before = ToDto(group);

        if (request.Name is not null)
        {
            group.Name = request.Name;
        }

        if (request.Description is not null)
        {
            group.Description = request.Description;
        }

        if (request.SimplifyDebts is { } simplify)
        {
            group.SimplifyDebts = simplify;
        }

        if (request.IsArchived is { } archived)
        {
            group.IsArchived = archived;
        }

        await WriteAuditLogAsync(group.Id, "Group", group.Id, "Updated", caller.Id, before, ToDto(group), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(group);
    }

    public async Task DeleteAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);
        RequireOwner(caller);

        group.IsDeleted = true;

        await WriteAuditLogAsync(group.Id, "Group", group.Id, "Deleted", caller.Id, null, null, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<GroupDto> GetBySharedTokenAsync(string shareToken, CancellationToken cancellationToken)
    {
        var group = await _groupRepository.GetByShareTokenAsync(shareToken, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Link chia sẻ không hợp lệ hoặc đã bị đổi.");

        return ToDto(group);
    }

    public async Task<string> RotateShareTokenAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);
        RequireOwner(caller);

        string newToken;
        do
        {
            newToken = _shareTokenGenerator.Generate();
        }
        while (await _groupRepository.ShareTokenExistsAsync(newToken, cancellationToken));

        group.ShareToken = newToken;

        await WriteAuditLogAsync(group.Id, "Group", group.Id, "Updated", caller.Id, null, new { ShareTokenRotated = true }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return newToken;
    }

    public async Task<GroupMemberDto> AddMemberAsync(Guid callerUserId, Guid groupId, AddMemberRequest request, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId); // mọi thành viên đều được thêm người mới

        if (request.UserId is null && string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Cần UserId (thành viên có tài khoản) hoặc DisplayName (khách vãng lai).");
        }

        var displayName = request.DisplayName ?? string.Empty;

        var member = new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = request.UserId,
            DisplayName = displayName,
            Role = GroupMemberRole.Member,
            IsActive = true,
            JoinedAt = DateTimeOffset.UtcNow,
        };

        await _groupRepository.AddMemberAsync(member, cancellationToken);
        await WriteAuditLogAsync(group.Id, "GroupMember", member.Id, "Created", caller.Id, null, ToMemberDto(member), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Thông báo "được thêm vào nhóm mới" (CLAUDE.md mục 13) — chỉ áp dụng khi thêm bằng UserId
        // (có tài khoản), không áp dụng cho khách vãng lai thêm bằng DisplayName.
        if (request.UserId is { } addedUserId)
        {
            var addedUser = await _userRepository.GetByIdAsync(addedUserId, cancellationToken);
            if (addedUser is not null)
            {
                await _notificationService.NotifyAsync(
                    [new NotificationRecipient(addedUserId, addedUser.Email)],
                    group.Id,
                    "MemberAdded",
                    "Bạn được thêm vào nhóm mới",
                    $"Bạn đã được thêm vào nhóm \"{group.Name}\".",
                    $"/Groups/Details/{group.Id}",
                    cancellationToken);
            }
        }

        return ToMemberDto(member);
    }

    public async Task<GroupMemberDto> UpdateMemberAsync(Guid callerUserId, Guid groupId, Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);
        var target = FindMember(group, memberId);

        // Đổi tên chính mình thì ai cũng được; đổi tên người khác chỉ Owner (CLAUDE.md mục 4.4).
        if (target.Id != caller.Id)
        {
            RequireOwner(caller);
        }

        var before = ToMemberDto(target);
        target.DisplayName = request.DisplayName;

        await WriteAuditLogAsync(group.Id, "GroupMember", target.Id, "Updated", caller.Id, before, ToMemberDto(target), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToMemberDto(target);
    }

    public async Task RemoveMemberAsync(Guid callerUserId, Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);
        var target = FindMember(group, memberId);

        if (target.Id != caller.Id)
        {
            RequireOwner(caller);
        }

        if (target.Role == GroupMemberRole.Owner)
        {
            var otherOwners = group.Members.Any(m => m.Id != target.Id && m.IsActive && m.Role == GroupMemberRole.Owner);
            if (!otherOwners)
            {
                throw new DomainException(ErrorCodes.LastOwnerCannotBeRemoved, "Nhóm phải có ít nhất 1 Owner.");
            }
        }

        var netBalance = await GetMemberNetBalanceAsync(group.Id, target.Id, cancellationToken);
        if (netBalance != 0)
        {
            throw new DomainException(
                ErrorCodes.MemberHasOutstandingBalance,
                $"Thành viên còn số dư {netBalance}đ chưa quyết toán, không thể rời/xóa khỏi nhóm.");
        }

        // Chụp lại tên trước khi xóa (CLAUDE.md mục 15.5) — để timeline biết CHÍNH XÁC ai đã rời/bị
        // xóa khỏi nhóm (khác với ActorMemberName vốn chỉ cho biết AI thực hiện hành động, có thể là
        // Owner xóa người khác chứ không phải chính người bị xóa).
        var before = ToMemberDto(target);
        target.IsActive = false;

        await WriteAuditLogAsync(group.Id, "GroupMember", target.Id, "Deleted", caller.Id, before, null, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<GroupMemberDto> UpdateMemberRoleAsync(Guid callerUserId, Guid groupId, Guid memberId, UpdateMemberRoleRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<GroupMemberRole>(request.Role, ignoreCase: true, out var newRole))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"Role '{request.Role}' không hợp lệ.");
        }

        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);
        RequireOwner(caller); // chỉ Owner mới được gán/thu hồi quyền Owner (CLAUDE.md mục 4.4)

        var target = FindMember(group, memberId);

        if (target.Role == GroupMemberRole.Owner && newRole == GroupMemberRole.Member)
        {
            var otherOwners = group.Members.Any(m => m.Id != target.Id && m.IsActive && m.Role == GroupMemberRole.Owner);
            if (!otherOwners)
            {
                throw new DomainException(ErrorCodes.LastOwnerCannotBeRemoved, "Nhóm phải có ít nhất 1 Owner.");
            }
        }

        var before = ToMemberDto(target);
        target.Role = newRole;

        await WriteAuditLogAsync(group.Id, "GroupMember", target.Id, "Updated", caller.Id, before, ToMemberDto(target), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToMemberDto(target);
    }

    public async Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(Guid callerUserId, Guid groupId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId); // mọi thành viên đều xem được lịch sử, không riêng Owner

        // Lấy theo cả thành viên đã rời nhóm (IsActive = false) để tên trong log cũ vẫn hiển thị đúng.
        var memberNames = group.Members.ToDictionary(m => m.Id, m => m.DisplayName);

        var (items, total) = await _auditLogRepository.GetPagedByGroupIdAsync(groupId, page, pageSize, cancellationToken);
        var dtos = items.Select(log => new AuditLogDto(
            log.Id,
            log.EntityType,
            log.EntityId,
            log.Action,
            log.ActorMemberId,
            memberNames.GetValueOrDefault(log.ActorMemberId, "(đã rời nhóm)"),
            log.BeforeJson,
            log.AfterJson,
            log.CreatedAt,
            BuildSummary(log, group.Currency, memberNames))).ToList();

        return new PagedResult<AuditLogDto>(dtos, page, pageSize, total);
    }

    /// <summary>
    /// Dựng mô tả 1 dòng tiếng Việt cho Timeline hoạt động nhóm (CLAUDE.md mục 15.5) từ
    /// EntityType/Action/Before/AfterJson. Parse lỗi (JSON không đúng shape mong đợi — dữ liệu cũ
    /// trước khi field này tồn tại, hoặc trường hợp bất thường khác) rơi về mô tả chung chung thay vì
    /// ném lỗi, để 1 dòng lịch sử hỏng không làm sập cả trang Timeline.
    /// </summary>
    private static string BuildSummary(AuditLog log, string currency, IReadOnlyDictionary<Guid, string> memberNames)
    {
        string MemberName(Guid memberId) => memberNames.GetValueOrDefault(memberId, "(đã rời nhóm)");
        T? Parse<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        try
        {
            switch (log.EntityType, log.Action)
            {
                case ("Expense", "Created"):
                    var created = Parse<ExpenseDto>(log.AfterJson);
                    return created is null ? "đã thêm 1 khoản chi" : $"đã thêm khoản chi \"{created.Title}\" ({SupportedCurrencies.Format(created.TotalAmount, currency)})";

                case ("Expense", "Updated"):
                    var updated = Parse<ExpenseDto>(log.AfterJson);
                    return updated is null ? "đã sửa 1 khoản chi" : $"đã sửa khoản chi \"{updated.Title}\"";

                case ("Expense", "Deleted"):
                    var deletedExpense = Parse<ExpenseDto>(log.BeforeJson);
                    return deletedExpense is null ? "đã xóa 1 khoản chi" : $"đã xóa khoản chi \"{deletedExpense.Title}\" ({SupportedCurrencies.Format(deletedExpense.TotalAmount, currency)})";

                case ("Settlement", "Created"):
                    var settlementCreated = Parse<SettlementDto>(log.AfterJson);
                    return settlementCreated is null
                        ? "đã ghi nhận 1 khoản thanh toán"
                        : $"đã ghi nhận chuyển {SupportedCurrencies.Format(settlementCreated.Amount, currency)} từ {MemberName(settlementCreated.FromMemberId)} đến {MemberName(settlementCreated.ToMemberId)}";

                case ("Settlement", "Updated"):
                    var settlementUpdated = Parse<SettlementDto>(log.AfterJson);
                    if (settlementUpdated is null)
                    {
                        return "đã cập nhật 1 khoản thanh toán";
                    }
                    var amountText = SupportedCurrencies.Format(settlementUpdated.Amount, currency);
                    return settlementUpdated.Status switch
                    {
                        "Confirmed" => $"đã xác nhận nhận {amountText} từ {MemberName(settlementUpdated.FromMemberId)}",
                        "Rejected" => $"đã từ chối khoản thanh toán {amountText} từ {MemberName(settlementUpdated.FromMemberId)}",
                        _ => $"đã cập nhật khoản thanh toán {amountText}",
                    };

                case ("Settlement", "Deleted"):
                    var settlementDeleted = Parse<SettlementDto>(log.BeforeJson);
                    return settlementDeleted is null
                        ? "đã xóa 1 khoản thanh toán"
                        : $"đã xóa khoản thanh toán {SupportedCurrencies.Format(settlementDeleted.Amount, currency)} (từ {MemberName(settlementDeleted.FromMemberId)} đến {MemberName(settlementDeleted.ToMemberId)})";

                case ("GroupMember", "Created"):
                    var memberCreated = Parse<GroupMemberDto>(log.AfterJson);
                    return memberCreated is null ? "đã thêm 1 thành viên" : $"đã thêm {memberCreated.DisplayName} vào nhóm";

                case ("GroupMember", "Updated"):
                    var memberBefore = Parse<GroupMemberDto>(log.BeforeJson);
                    var memberAfter = Parse<GroupMemberDto>(log.AfterJson);
                    if (memberBefore is not null && memberAfter is not null && memberBefore.DisplayName != memberAfter.DisplayName)
                    {
                        return $"đã đổi tên \"{memberBefore.DisplayName}\" thành \"{memberAfter.DisplayName}\"";
                    }
                    if (memberBefore is not null && memberAfter is not null && memberBefore.Role != memberAfter.Role)
                    {
                        return $"đã đổi vai trò của {memberAfter.DisplayName} thành {memberAfter.Role}";
                    }
                    return memberAfter is null ? "đã cập nhật 1 thành viên" : $"đã cập nhật thông tin của {memberAfter.DisplayName}";

                case ("GroupMember", "Deleted"):
                    var memberDeleted = Parse<GroupMemberDto>(log.BeforeJson);
                    if (memberDeleted is null)
                    {
                        return "đã xóa 1 thành viên khỏi nhóm";
                    }
                    return log.ActorMemberId == log.EntityId
                        ? $"{memberDeleted.DisplayName} đã rời khỏi nhóm"
                        : $"đã xóa {memberDeleted.DisplayName} khỏi nhóm";

                case ("Group", "Created"):
                    return "đã tạo nhóm";

                case ("Group", "Updated"):
                    return log.AfterJson?.Contains("ShareTokenRotated", StringComparison.OrdinalIgnoreCase) == true
                        ? "đã đổi link chia sẻ nhóm"
                        : "đã cập nhật thông tin nhóm";

                case ("Group", "Deleted"):
                    return "đã xóa nhóm";

                default:
                    return $"{log.Action} {log.EntityType}";
            }
        }
        catch
        {
            return $"{log.Action} {log.EntityType}";
        }
    }

    private async Task<long> GetMemberNetBalanceAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        var expenses = await _expenseRepository.GetAllByGroupIdAsync(groupId, cancellationToken);
        var settlements = await _settlementRepository.GetAllByGroupIdAsync(groupId, cancellationToken);

        var expenseInputs = expenses.Select(e => new ExpenseBalanceInput(
            e.Id,
            e.Payers.Select(p => new MemberAmount(p.GroupMemberId, p.Amount)).ToList(),
            e.Splits.Select(s => new MemberAmount(s.GroupMemberId, s.Amount)).ToList()));

        var settlementInputs = settlements.Select(s => new SettlementBalanceInput(s.FromMemberId, s.ToMemberId, s.Amount, s.Status));

        var balances = _balanceCalculator.Calculate(expenseInputs, settlementInputs);
        return balances.FirstOrDefault(b => b.MemberId == memberId)?.Net ?? 0;
    }

    private async Task<Group> LoadGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await _groupRepository.GetByIdWithMembersAsync(groupId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Không tìm thấy nhóm.");

    private static GroupMember ResolveCallerMember(Group group, Guid callerUserId) =>
        group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive)
            ?? throw new DomainException(ErrorCodes.MemberNotInGroup, "Bạn không phải thành viên của nhóm này.");

    private static GroupMember FindMember(Group group, Guid memberId) =>
        group.Members.FirstOrDefault(m => m.Id == memberId)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Không tìm thấy thành viên.");

    private static void RequireOwner(GroupMember member)
    {
        if (member.Role != GroupMemberRole.Owner)
        {
            throw new DomainException(ErrorCodes.InsufficientRole, "Chỉ Owner của nhóm mới được thực hiện thao tác này.");
        }
    }

    private async Task WriteAuditLogAsync(
        Guid groupId, string entityType, Guid entityId, string action, Guid actorMemberId,
        object? before, object? after, CancellationToken cancellationToken)
    {
        await _auditLogRepository.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorMemberId = actorMemberId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }

    private static GroupDto ToDto(Group group) => new(
        group.Id,
        group.Name,
        group.Description,
        group.Type.ToString(),
        group.Currency,
        group.SimplifyDebts,
        group.IsArchived,
        group.ShareToken,
        group.Members.Where(m => m.IsActive).Select(ToMemberDto).ToList());

    private static GroupMemberDto ToMemberDto(GroupMember member) =>
        new(member.Id, member.UserId, member.DisplayName, member.Role.ToString(), member.IsActive);
}
