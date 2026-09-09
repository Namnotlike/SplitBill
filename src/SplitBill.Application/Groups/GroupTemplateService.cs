using System.Text.Json;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Groups;

/// <summary>Cài đặt CLAUDE.md mục 25.6. Là tài nguyên CỦA RIÊNG 1 User (không phải của 1 Group như
/// <see cref="Expenses.SplitPresetService"/>/<see cref="Expenses.ExpenseCommentService"/>) — quyền
/// hạn chỉ đơn giản so khớp <see cref="GroupTemplate.CreatedByUserId"/> với caller, không cần khái
/// niệm GroupMemberRole. Việc tạo Group từ mẫu hoàn toàn KHÔNG viết lại logic tạo nhóm/thêm thành
/// viên nào — gọi thẳng lại <see cref="IGroupService"/> đã có, cùng nguyên tắc "orchestrate service có
/// sẵn" đã áp dụng cho Export (mục 25.4) và Dashboard (mục 25.5).</summary>
public sealed class GroupTemplateService : IGroupTemplateService
{
    private readonly IGroupTemplateRepository _templateRepository;
    private readonly IGroupService _groupService;
    private readonly IUnitOfWork _unitOfWork;

    public GroupTemplateService(IGroupTemplateRepository templateRepository, IGroupService groupService, IUnitOfWork unitOfWork)
    {
        _templateRepository = templateRepository;
        _groupService = groupService;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<GroupTemplateDto>> GetMyTemplatesAsync(Guid callerUserId, CancellationToken cancellationToken)
    {
        var templates = await _templateRepository.GetByCreatedByUserIdAsync(callerUserId, cancellationToken);
        return templates.OrderByDescending(t => t.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<GroupTemplateDto> CreateAsync(Guid callerUserId, CreateGroupTemplateRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<GroupType>(request.Type, ignoreCase: true, out _))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"GroupType '{request.Type}' không hợp lệ.");
        }

        var template = new GroupTemplate
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = callerUserId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? Common.SupportedCurrencies.Default : request.Currency,
            SimplifyDebts = request.SimplifyDebts,
            MemberNamesJson = JsonSerializer.Serialize(request.MemberNames ?? []),
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _templateRepository.AddAsync(template, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(template);
    }

    public async Task DeleteAsync(Guid callerUserId, Guid templateId, CancellationToken cancellationToken)
    {
        var template = await LoadOwnedTemplateAsync(callerUserId, templateId, cancellationToken);
        template.IsDeleted = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<GroupDto> CreateGroupFromTemplateAsync(Guid callerUserId, Guid templateId, CreateGroupFromTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await LoadOwnedTemplateAsync(callerUserId, templateId, cancellationToken);

        var groupName = string.IsNullOrWhiteSpace(request.GroupName) ? template.Name : request.GroupName;
        var group = await _groupService.CreateAsync(
            callerUserId,
            new CreateGroupRequest(groupName, template.Description, template.Type, template.Currency),
            cancellationToken);

        // CreateAsync (GroupService) luôn khởi tạo SimplifyDebts = true (CLAUDE.md mục 25.1 dòng
        // "SimplifyDebts = true" trong GroupService.CreateAsync) — chỉ cần gọi UpdateAsync nếu mẫu
        // muốn tắt, tránh 1 lượt ghi + audit log "Updated" thừa cho trường hợp phổ biến hơn (giữ mặc định).
        if (!template.SimplifyDebts)
        {
            await _groupService.UpdateAsync(callerUserId, group.Id, new UpdateGroupRequest(null, null, false, null), cancellationToken);
        }

        var memberNames = JsonSerializer.Deserialize<List<string>>(template.MemberNamesJson) ?? [];
        foreach (var name in memberNames)
        {
            await _groupService.AddMemberAsync(callerUserId, group.Id, new AddMemberRequest(null, name), cancellationToken);
        }

        // Đọc lại từ đầu để trả về đúng danh sách Members đầy đủ (đã thêm xong toàn bộ khách vãng lai)
        // và SimplifyDebts đã cập nhật, thay vì trả `group` cũ (chụp ngay sau CreateAsync, chưa có gì).
        return await _groupService.GetByIdAsync(callerUserId, group.Id, cancellationToken);
    }

    private async Task<GroupTemplate> LoadOwnedTemplateAsync(Guid callerUserId, Guid templateId, CancellationToken cancellationToken)
    {
        var template = await _templateRepository.GetByIdAsync(templateId, cancellationToken);
        // Không phân biệt "không tồn tại" với "tồn tại nhưng không phải của caller" — mẫu là tài
        // nguyên riêng tư 1 người, fail closed bằng đúng 1 lỗi để không lộ thêm thông tin.
        if (template is null || template.CreatedByUserId != callerUserId)
        {
            throw new DomainException(ErrorCodes.GroupTemplateNotFound, "Không tìm thấy mẫu nhóm.");
        }

        return template;
    }

    private static GroupTemplateDto ToDto(GroupTemplate template)
    {
        var memberNames = JsonSerializer.Deserialize<List<string>>(template.MemberNamesJson) ?? [];
        return new GroupTemplateDto(
            template.Id, template.Name, template.Description, template.Type, template.Currency,
            template.SimplifyDebts, memberNames, template.CreatedAt);
    }
}
