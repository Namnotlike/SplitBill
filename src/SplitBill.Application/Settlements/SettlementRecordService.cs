using System.Text.Json;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Notifications;
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
    private readonly INotificationService _notificationService;

    public SettlementRecordService(
        IGroupRepository groupRepository,
        ISettlementRepository settlementRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        INotificationService notificationService)
    {
        _groupRepository = groupRepository;
        _settlementRepository = settlementRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
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

        // Thông báo "có người ghi nhận đã chuyển tiền cho mình" (CLAUDE.md mục 13) — báo cho người NHẬN.
        await NotifyMemberAsync(
            group, request.ToMemberId, group.Id, "SettlementRecorded", "Có người ghi nhận đã chuyển tiền",
            $"{caller.DisplayName} vừa ghi nhận đã chuyển {settlement.Amount:N0}đ cho bạn trong nhóm \"{group.Name}\". Vui lòng xác nhận.",
            $"/Groups/SettlementPlan/{group.Id}", cancellationToken);

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

        // Thông báo "settlement của mình được xác nhận" (CLAUDE.md mục 13) — báo cho người GỬI.
        await NotifyMemberAsync(
            group, settlement.FromMemberId, group.Id, "SettlementConfirmed", "Thanh toán đã được xác nhận",
            $"{caller.DisplayName} đã xác nhận nhận {settlement.Amount:N0}đ từ bạn trong nhóm \"{group.Name}\".",
            $"/Groups/SettlementPlan/{group.Id}", cancellationToken);

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

        // Thông báo "settlement của mình bị từ chối" (CLAUDE.md mục 13) — báo cho người GỬI.
        await NotifyMemberAsync(
            group, settlement.FromMemberId, group.Id, "SettlementRejected", "Thanh toán bị từ chối",
            $"{caller.DisplayName} đã từ chối xác nhận {settlement.Amount:N0}đ từ bạn trong nhóm \"{group.Name}\". Vui lòng kiểm tra lại.",
            $"/Groups/SettlementPlan/{group.Id}", cancellationToken);

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

        // Chụp lại trạng thái trước khi xóa (CLAUDE.md mục 15.5) — để timeline/audit log biết đã xóa
        // settlement nào (từ ai, cho ai, bao nhiêu tiền) thay vì chỉ biết "có 1 settlement bị xóa".
        var before = ToDto(settlement);

        settlement.IsDeleted = true;

        await WriteAuditLogAsync(group.Id, settlement.Id, "Deleted", caller.Id, before, null, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SettlementDto>> GetDeletedAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var settlements = await _settlementRepository.GetDeletedByGroupIdAsync(groupId, cancellationToken);
        return settlements.Select(ToDto).ToList();
    }

    // CLAUDE.md mục 24 — khôi phục settlement đã xóa. Quyền hạn mirror đúng DeleteAsync (chỉ người ghi
    // nhận hoặc Owner) — chỉ Settlement đang Pending mới xóa được (xem DeleteAsync ở trên), nên Status
    // vẫn còn nguyên giá trị Pending sau khi khôi phục, không cần gán lại.
    public async Task<SettlementDto> RestoreAsync(Guid callerUserId, Guid settlementId, CancellationToken cancellationToken)
    {
        var settlement = await _settlementRepository.GetByIdIncludingDeletedAsync(settlementId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Không tìm thấy settlement.");
        var group = await LoadGroupAsync(settlement.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (!settlement.IsDeleted)
        {
            throw new DomainException(ErrorCodes.SettlementNotDeleted, "Settlement này chưa bị xóa.");
        }

        if (caller.Id != settlement.RecordedByMemberId && caller.Role != GroupMemberRole.Owner)
        {
            throw new DomainException(ErrorCodes.InsufficientRole, "Chỉ người ghi nhận hoặc Owner mới được khôi phục.");
        }

        settlement.IsDeleted = false;

        await WriteAuditLogAsync(group.Id, settlement.Id, "Restored", caller.Id, null, ToDto(settlement), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(settlement);
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

    /// <summary>Gửi thông báo cho 1 GroupMember cụ thể — chỉ gửi nếu member đó có tài khoản
    /// (CLAUDE.md mục 13: khách vãng lai không bao giờ nhận thông báo).</summary>
    private Task NotifyMemberAsync(
        Group group, Guid memberId, Guid groupId, string type, string title, string message, string? linkUrl,
        CancellationToken cancellationToken)
    {
        var member = group.Members.FirstOrDefault(m => m.Id == memberId);
        if (member?.User is null)
        {
            return Task.CompletedTask;
        }

        return _notificationService.NotifyAsync(
            [new NotificationRecipient(member.User.Id, member.User.Email)],
            groupId, type, title, message, linkUrl, cancellationToken);
    }

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
