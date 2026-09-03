using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IGroupRepository
{
    /// <summary>Nạp Group kèm Members (không .IgnoreQueryFilters — chỉ nhóm chưa xóa).</summary>
    Task<Group?> GetByIdWithMembersAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>Nạp Group kèm Members theo ShareToken — dùng cho link chia sẻ (CLAUDE.md mục 8).</summary>
    Task<Group?> GetByShareTokenAsync(string shareToken, CancellationToken cancellationToken);

    /// <summary>Danh sách nhóm mà user hiện tại là thành viên đang active.</summary>
    Task<List<Group>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(Group group, CancellationToken cancellationToken);

    Task<bool> ShareTokenExistsAsync(string shareToken, CancellationToken cancellationToken);

    Task<GroupMember?> GetMemberByIdAsync(Guid groupId, Guid memberId, CancellationToken cancellationToken);

    Task AddMemberAsync(GroupMember member, CancellationToken cancellationToken);
}
