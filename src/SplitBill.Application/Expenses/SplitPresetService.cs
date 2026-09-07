using System.Text.Json;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Expenses;

/// <summary>Cài đặt CLAUDE.md mục 21. Tách khỏi <see cref="ExpenseService"/> — tự có
/// LoadGroupAsync/ResolveCallerMember riêng, cùng mẫu <see cref="ExpenseCommentService"/> (mục 19).</summary>
public sealed class SplitPresetService : ISplitPresetService
{
    private readonly ISplitPresetRepository _presetRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SplitPresetService(ISplitPresetRepository presetRepository, IGroupRepository groupRepository, IUnitOfWork unitOfWork)
    {
        _presetRepository = presetRepository;
        _groupRepository = groupRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<SplitPresetDto>> GetByGroupIdAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var presets = await _presetRepository.GetByGroupIdAsync(groupId, cancellationToken);
        return presets.OrderByDescending(p => p.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<SplitPresetDto> CreateAsync(Guid callerUserId, Guid groupId, CreateSplitPresetRequest request, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (!Enum.TryParse<SplitMode>(request.SplitMode, ignoreCase: true, out _))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"SplitMode '{request.SplitMode}' không hợp lệ.");
        }

        // Preset MỚI hoàn toàn -> mọi thành viên được tham chiếu phải đang active, cùng nguyên tắc
        // ValidateMembersBelongToGroup/ValidateMembersAreActive ở ExpenseService (mục 5.4) — không có
        // khái niệm "tham chiếu cũ được miễn trừ" vì preset không có API Update, chỉ Create/Delete.
        // 2 bước tách riêng (không gộp chung) để phân biệt đúng "không tồn tại trong nhóm" (kể cả
        // chưa từng là thành viên) với "từng là thành viên nhưng đã rời" — 2 lỗi khác nghĩa nhau.
        var allMemberIds = group.Members.Select(m => m.Id).ToHashSet();
        var activeMemberIds = group.Members.Where(m => m.IsActive).Select(m => m.Id).ToHashSet();
        var referencedMemberIds = (request.SplitConfig.MemberIds ?? [])
            .Concat(request.SplitConfig.Shares?.Select(s => s.MemberId) ?? [])
            .Concat(request.SplitConfig.Percentages?.Select(p => p.MemberId) ?? [])
            .Concat(request.SplitConfig.ExactAmounts?.Select(e => e.MemberId) ?? [])
            .Concat(request.SplitConfig.Items?.SelectMany(i => i.ConsumerMemberIds) ?? [])
            .Distinct();

        foreach (var memberId in referencedMemberIds)
        {
            if (!allMemberIds.Contains(memberId))
            {
                throw new DomainException(ErrorCodes.MemberNotInGroup, $"GroupMemberId {memberId} không thuộc nhóm này.");
            }

            if (!activeMemberIds.Contains(memberId))
            {
                throw new DomainException(ErrorCodes.MemberNotActive, $"Thành viên {memberId} không còn active trong nhóm, không thể lưu vào preset.");
            }
        }

        var preset = new SplitPreset
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Name = request.Name,
            SplitMode = request.SplitMode,
            SplitConfigJson = JsonSerializer.Serialize(request.SplitConfig),
            CreatedByMemberId = caller.Id,
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _presetRepository.AddAsync(preset, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(preset);
    }

    public async Task DeleteAsync(Guid callerUserId, Guid presetId, CancellationToken cancellationToken)
    {
        var preset = await _presetRepository.GetByIdAsync(presetId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.SplitPresetNotFound, "Không tìm thấy preset.");
        var group = await LoadGroupAsync(preset.GroupId, cancellationToken);
        var caller = ResolveCallerMember(group, callerUserId);

        if (preset.CreatedByMemberId != caller.Id && caller.Role != GroupMemberRole.Owner)
        {
            throw new DomainException(ErrorCodes.InsufficientRole, "Chỉ tác giả preset hoặc Owner của nhóm mới xóa được.");
        }

        preset.IsDeleted = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Group> LoadGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await _groupRepository.GetByIdWithMembersAsync(groupId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Không tìm thấy nhóm.");

    private static GroupMember ResolveCallerMember(Group group, Guid callerUserId) =>
        group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive)
            ?? throw new DomainException(ErrorCodes.MemberNotInGroup, "Bạn không phải thành viên của nhóm này.");

    private static SplitPresetDto ToDto(SplitPreset preset)
    {
        var config = JsonSerializer.Deserialize<SplitConfigInput>(preset.SplitConfigJson) ?? new SplitConfigInput();
        return new SplitPresetDto(preset.Id, preset.GroupId, preset.Name, preset.SplitMode, config, preset.CreatedAt);
    }
}
