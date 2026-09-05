namespace SplitBill.Application.Groups;

public sealed record GroupMemberDto(Guid Id, Guid? UserId, string DisplayName, string Role, bool IsActive);

public sealed record GroupDto(
    Guid Id,
    string Name,
    string? Description,
    string Type,
    string Currency,
    bool SimplifyDebts,
    bool IsArchived,
    string ShareToken,
    IReadOnlyList<GroupMemberDto> Members);

public sealed record GroupSummaryDto(Guid Id, string Name, string Type, bool IsArchived, string Currency);

public sealed record CreateGroupRequest(string Name, string? Description, string Type, string? Currency);

public sealed record UpdateGroupRequest(string? Name, string? Description, bool? SimplifyDebts, bool? IsArchived);

/// <summary>Thêm thành viên: truyền UserId để gắn tài khoản có sẵn, hoặc chỉ DisplayName cho khách vãng lai.</summary>
public sealed record AddMemberRequest(Guid? UserId, string? DisplayName);

public sealed record UpdateMemberRequest(string DisplayName);

/// <summary>Gán/thu hồi quyền Owner cho 1 thành viên — chỉ Owner gọi được (CLAUDE.md mục 4.4).</summary>
public sealed record UpdateMemberRoleRequest(string Role);

/// <summary>1 dòng lịch sử thay đổi, trả qua GET /groups/{id}/audit-logs (CLAUDE.md mục 8).</summary>
public sealed record AuditLogDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    Guid ActorMemberId,
    string ActorMemberName,
    string? BeforeJson,
    string? AfterJson,
    DateTimeOffset CreatedAt);
