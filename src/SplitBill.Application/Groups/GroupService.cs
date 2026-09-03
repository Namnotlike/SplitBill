using System.Text.Json;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Settlement;
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

    public GroupService(
        IGroupRepository groupRepository,
        IUserRepository userRepository,
        IExpenseRepository expenseRepository,
        ISettlementRepository settlementRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IShareTokenGenerator shareTokenGenerator,
        IBalanceCalculator balanceCalculator)
    {
        _groupRepository = groupRepository;
        _userRepository = userRepository;
        _expenseRepository = expenseRepository;
        _settlementRepository = settlementRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _shareTokenGenerator = shareTokenGenerator;
        _balanceCalculator = balanceCalculator;
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
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency,
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
        return groups.Select(g => new GroupSummaryDto(g.Id, g.Name, g.Type.ToString(), g.IsArchived)).ToList();
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

        target.IsActive = false;

        await WriteAuditLogAsync(group.Id, "GroupMember", target.Id, "Deleted", caller.Id, null, null, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
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
