using System.Text.Json;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;
using SettlementEntity = SplitBill.Domain.Entities.Settlement;

namespace SplitBill.Application.Settlements;

/// <summary>Cài đặt CLAUDE.md mục 8 — ghi nhận/xác nhận/từ chối/xóa thanh toán.</summary>
public sealed class SettlementRecordService : ISettlementRecordService
{
    private readonly IGroupRepository _groupRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SettlementRecordService(
        IGroupRepository groupRepository,
        ISettlementRepository settlementRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork)
    {
        _groupRepository = groupRepository;
        _settlementRepository = settlementRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<SettlementDto> CreateAsync(Guid callerUserId, Guid groupId, CreateSettlementRequest request, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (request.FromMemberId == request.ToMemberId)
        {
            throw new DomainException(ErrorCodes.SettlementSameMember, "FromMemberId và ToMemberId không được trùng nhau.");
        }

        if (request.Amount <= 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Amount phải > 0.");
        }

        RequireMemberInGroup(group, request.FromMemberId);
        RequireMemberInGroup(group, request.ToMemberId);

        var settlement = new SettlementEntity
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            FromMemberId = request.FromMemberId,
            ToMemberId = request.ToMemberId,
            Amount = request.Amount,
            Status = SettlementStatus.Pending,
            RecordedByMemberId = caller.Id,
            Note = request.Note,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _settlementRepository.AddAsync(settlement, cancellationToken);
        await WriteAuditLogAsync(group.Id, settlement.Id, "Created", caller.Id, null, ToDto(settlement), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(settlement);
    }

    public async Task<SettlementDto> ConfirmAsync(Guid callerUserId, Guid settlementId, CancellationToken cancellationToken)
    {
        var settlement = await LoadSettlementAsync(settlementId, cancellationToken);
        var group = await LoadGroupAsync(settlement.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (settlement.Status != SettlementStatus.Pending)
        {
            throw new DomainException(ErrorCodes.SettlementNotPending, "Settlement không còn ở trạng thái Pending.");
        }

        // Chỉ người NHẬN (ToMemberId) mới được xác nhận (CLAUDE.md mục 8).
        if (caller.Id != settlement.ToMemberId)
        {
            throw new DomainException(ErrorCodes.InsufficientRole, "Chỉ người nhận mới được xác nhận thanh toán.");
        }

        var before = ToDto(settlement);
        settlement.Status = SettlementStatus.Confirmed;
        settlement.ConfirmedByMemberId = caller.Id;
        settlement.ConfirmedAt = DateTimeOffset.UtcNow;

        await WriteAuditLogAsync(group.Id, settlement.Id, "Updated", caller.Id, before, ToDto(settlement), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(settlement);
    }

    public async Task<SettlementDto> RejectAsync(Guid callerUserId, Guid settlementId, CancellationToken cancellationToken)
    {
        var settlement = await LoadSettlementAsync(settlementId, cancellationToken);
        var group = await LoadGroupAsync(settlement.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (settlement.Status != SettlementStatus.Pending)
        {
            throw new DomainException(ErrorCodes.SettlementNotPending, "Settlement không còn ở trạng thái Pending.");
        }

        if (caller.Id != settlement.ToMemberId)
        {
            throw new DomainException(ErrorCodes.InsufficientRole, "Chỉ người nhận mới được từ chối thanh toán.");
        }

        var before = ToDto(settlement);
        settlement.Status = SettlementStatus.Rejected;

        await WriteAuditLogAsync(group.Id, settlement.Id, "Updated", caller.Id, before, ToDto(settlement), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(settlement);
    }

    public async Task DeleteAsync(Guid callerUserId, Guid settlementId, CancellationToken cancellationToken)
    {
        var settlement = await LoadSettlementAsync(settlementId, cancellationToken);
        var group = await LoadGroupAsync(settlement.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (settlement.Status != SettlementStatus.Pending)
        {
            throw new DomainException(ErrorCodes.SettlementNotPending, "Chỉ xóa được settlement đang Pending.");
        }

        if (caller.Id != settlement.RecordedByMemberId && caller.Role != GroupMemberRole.Owner)
        {
            throw new DomainException(ErrorCodes.InsufficientRole, "Chỉ người ghi nhận hoặc Owner mới được xóa.");
        }

        settlement.IsDeleted = true;

        await WriteAuditLogAsync(group.Id, settlement.Id, "Deleted", caller.Id, null, null, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SettlementDto>> GetByGroupAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var settlements = await _settlementRepository.GetAllByGroupIdAsync(groupId, cancellationToken);
        return settlements.OrderByDescending(s => s.CreatedAt).Select(ToDto).ToList();
    }

    private async Task<SettlementEntity> LoadSettlementAsync(Guid settlementId, CancellationToken cancellationToken) =>
        await _settlementRepository.GetByIdAsync(settlementId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Không tìm thấy settlement.");

    private async Task<Group> LoadGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await _groupRepository.GetByIdWithMembersAsync(groupId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Không tìm thấy nhóm.");

    private static GroupMember ResolveCallerMember(Group group, Guid callerUserId) =>
        group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive)
            ?? throw new DomainException(ErrorCodes.MemberNotInGroup, "Bạn không phải thành viên của nhóm này.");

    private static void RequireMemberInGroup(Group group, Guid memberId)
    {
        if (group.Members.All(m => m.Id != memberId))
        {
            throw new DomainException(ErrorCodes.MemberNotInGroup, $"GroupMemberId {memberId} không thuộc nhóm này.");
        }
    }

    private async Task WriteAuditLogAsync(
        Guid groupId, Guid settlementId, string action, Guid actorMemberId,
        object? before, object? after, CancellationToken cancellationToken)
    {
        await _auditLogRepository.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            EntityType = "Settlement",
            EntityId = settlementId,
            Action = action,
            ActorMemberId = actorMemberId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }

    private static SettlementDto ToDto(SettlementEntity settlement) => new(
        settlement.Id,
        settlement.GroupId,
        settlement.FromMemberId,
        settlement.ToMemberId,
        settlement.Amount,
        settlement.Status.ToString(),
        settlement.Note,
        settlement.RecordedByMemberId,
        settlement.ConfirmedByMemberId,
        settlement.ConfirmedAt,
        settlement.CreatedAt);
}
