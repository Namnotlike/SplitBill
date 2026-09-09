namespace SplitBill.Application.Groups;

/// <summary>Mẫu nhóm tái sử dụng (CLAUDE.md mục 25.6).</summary>
public sealed record GroupTemplateDto(
    Guid Id,
    string Name,
    string? Description,
    string Type,
    string Currency,
    bool SimplifyDebts,
    IReadOnlyList<string> MemberNames,
    DateTimeOffset CreatedAt);

public sealed record CreateGroupTemplateRequest(
    string Name,
    string? Description,
    string Type,
    string? Currency,
    bool SimplifyDebts,
    IReadOnlyList<string>? MemberNames);

/// <summary>Tạo nhóm mới từ 1 mẫu — GroupName rỗng/null thì dùng nguyên Name của mẫu.</summary>
public sealed record CreateGroupFromTemplateRequest(string? GroupName);
