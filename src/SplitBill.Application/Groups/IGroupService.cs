using SplitBill.Application.Expenses; // PagedResult<T> — dùng chung, xem ghi chú tại nơi định nghĩa

namespace SplitBill.Application.Groups;

public interface IGroupService
{
    Task<GroupDto> CreateAsync(Guid callerUserId, CreateGroupRequest request, CancellationToken cancellationToken);

    /// <summary>Nhân bản nhóm (CLAUDE.md mục 18) — tạo nhóm MỚI copy Name/Description/Type/Currency/
    /// SimplifyDebts + toàn bộ thành viên đang active (kể cả khách vãng lai) từ nhóm nguồn, KHÔNG copy
    /// Expense/Settlement/AuditLog/RecurringExpenseTemplate/ShareToken (nhóm mới có ShareToken riêng).
    /// Caller phải là thành viên đang active của nhóm nguồn (không nhất thiết Owner), và luôn trở
    /// thành Owner của nhóm mới — mọi thành viên khác được copy sang với Role Member.</summary>
    Task<GroupDto> DuplicateAsync(Guid callerUserId, Guid sourceGroupId, DuplicateGroupRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<GroupSummaryDto>> GetMyGroupsAsync(Guid callerUserId, CancellationToken cancellationToken);
    Task<GroupDto> GetByIdAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);
    Task<GroupDto> UpdateAsync(Guid callerUserId, Guid groupId, UpdateGroupRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Xem nhóm qua link chia sẻ — không cần đăng nhập, read-only (CLAUDE.md mục 8).</summary>
    Task<GroupDto> GetBySharedTokenAsync(string shareToken, CancellationToken cancellationToken);

    /// <summary>Đổi token chia sẻ — chỉ Owner (CLAUDE.md mục 8, 4.4).</summary>
    Task<string> RotateShareTokenAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Tự thêm mình vào nhóm qua link chia sẻ — CHỈ dành cho user đã đăng nhập (CLAUDE.md
    /// mục 15.6). Nếu user từng là thành viên rồi rời nhóm, kích hoạt lại CÙNG GroupMemberId thay vì
    /// tạo mới (unique index (GroupId, UserId) lọc theo UserId, không theo IsActive — CLAUDE.md mục
    /// 4.3 — nên không thể tạo hàng mới trùng UserId dù hàng cũ đã IsActive=false).</summary>
    Task<GroupMemberDto> JoinViaShareTokenAsync(Guid callerUserId, string shareToken, CancellationToken cancellationToken);

    Task<GroupMemberDto> AddMemberAsync(Guid callerUserId, Guid groupId, AddMemberRequest request, CancellationToken cancellationToken);
    Task<GroupMemberDto> UpdateMemberAsync(Guid callerUserId, Guid groupId, Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken);
    Task RemoveMemberAsync(Guid callerUserId, Guid groupId, Guid memberId, CancellationToken cancellationToken);

    /// <summary>Gán/thu hồi quyền Owner — chỉ Owner gọi được, không cho hạ quyền Owner cuối cùng (CLAUDE.md mục 4.4).</summary>
    Task<GroupMemberDto> UpdateMemberRoleAsync(Guid callerUserId, Guid groupId, Guid memberId, UpdateMemberRoleRequest request, CancellationToken cancellationToken);

    /// <summary>Lịch sử thay đổi của nhóm, phân trang mới nhất trước (CLAUDE.md mục 8).</summary>
    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(Guid callerUserId, Guid groupId, int page, int pageSize, CancellationToken cancellationToken);
}
