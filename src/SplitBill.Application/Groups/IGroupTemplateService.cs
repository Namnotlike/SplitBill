namespace SplitBill.Application.Groups;

/// <summary>CLAUDE.md mục 25.6 — Mẫu nhóm tái sử dụng.</summary>
public interface IGroupTemplateService
{
    Task<IReadOnlyList<GroupTemplateDto>> GetMyTemplatesAsync(Guid callerUserId, CancellationToken cancellationToken);

    Task<GroupTemplateDto> CreateAsync(Guid callerUserId, CreateGroupTemplateRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(Guid callerUserId, Guid templateId, CancellationToken cancellationToken);

    /// <summary>Tạo 1 Group mới từ mẫu — reuse nguyên IGroupService.CreateAsync/AddMemberAsync/
    /// UpdateAsync, không viết lại logic tạo nhóm/thêm thành viên nào.</summary>
    Task<GroupDto> CreateGroupFromTemplateAsync(Guid callerUserId, Guid templateId, CreateGroupFromTemplateRequest request, CancellationToken cancellationToken);
}
