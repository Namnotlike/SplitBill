using SplitBill.Application.Expenses; // PagedResult<T> — dùng chung, xem ghi chú tại nơi định nghĩa

namespace SplitBill.Application.Groups;

public interface IGroupService
{
    Task<GroupDto> CreateAsync(Guid callerUserId, CreateGroupRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupSummaryDto>> GetMyGroupsAsync(Guid callerUserId, CancellationToken cancellationToken);
    Task<GroupDto> GetByIdAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);
    Task<GroupDto> UpdateAsync(Guid callerUserId, Guid groupId, UpdateGroupRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Xem nhóm qua link chia sẻ — không cần đăng nhập, read-only (CLAUDE.md mục 8).</summary>
    Task<GroupDto> GetBySharedTokenAsync(string shareToken, CancellationToken cancellationToken);

    /// <summary>Đổi token chia sẻ — chỉ Owner (CLAUDE.md mục 8, 4.4).</summary>
    Task<string> RotateShareTokenAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    Task<GroupMemberDto> AddMemberAsync(Guid callerUserId, Guid groupId, AddMemberRequest request, CancellationToken cancellationToken);
    Task<GroupMemberDto> UpdateMemberAsync(Guid callerUserId, Guid groupId, Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken);
    Task RemoveMemberAsync(Guid callerUserId, Guid groupId, Guid memberId, CancellationToken cancellationToken);

    /// <summary>Gán/thu hồi quyền Owner — chỉ Owner gọi được, không cho hạ quyền Owner cuối cùng (CLAUDE.md mục 4.4).</summary>
    Task<GroupMemberDto> UpdateMemberRoleAsync(Guid callerUserId, Guid groupId, Guid memberId, UpdateMemberRoleRequest request, CancellationToken cancellationToken);

    /// <summary>Lịch sử thay đổi của nhóm, phân trang mới nhất trước (CLAUDE.md mục 8).</summary>
    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(Guid callerUserId, Guid groupId, int page, int pageSize, CancellationToken cancellationToken);
}
