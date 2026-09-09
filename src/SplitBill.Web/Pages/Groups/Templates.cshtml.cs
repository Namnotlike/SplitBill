using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Common;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

/// <summary>CLAUDE.md mục 25.6 — Mẫu nhóm tái sử dụng. Trang này KHÔNG thuộc 1 Group cụ thể nào
/// (khác Groups/Details) — mẫu là tài nguyên riêng của user hiện tại, nên đặt cạnh Groups/Index.</summary>
public class TemplatesModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public TemplatesModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public IReadOnlyList<GroupTemplateDto> MyTemplates { get; set; } = Array.Empty<GroupTemplateDto>();

    [BindProperty]
    public CreateTemplateInput NewTemplate { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public sealed class CreateTemplateInput
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public string Type { get; set; } = "OneTime";

        [Required]
        public string Currency { get; set; } = SupportedCurrencies.Default;

        public bool SimplifyDebts { get; set; } = true;

        // Mỗi tên 1 dòng — parse ở server (BuildMemberNames), không cần JS động như SplitPreset
        // (mục 21) vì đây chỉ là danh sách tên phẳng, không có cấu trúc SplitConfig phức tạp.
        public string? MemberNamesText { get; set; }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        MyTemplates = await _apiClient.GetMyGroupTemplatesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            MyTemplates = await _apiClient.GetMyGroupTemplatesAsync(cancellationToken);
            return Page();
        }

        try
        {
            var memberNames = BuildMemberNames(NewTemplate.MemberNamesText);
            await _apiClient.CreateGroupTemplateAsync(new CreateGroupTemplateRequest(
                NewTemplate.Name, NewTemplate.Description, NewTemplate.Type, NewTemplate.Currency,
                NewTemplate.SimplifyDebts, memberNames), cancellationToken);
            SuccessMessage = $"Đã lưu mẫu \"{NewTemplate.Name}\".";
            NewTemplate = new CreateTemplateInput();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }

        MyTemplates = await _apiClient.GetMyGroupTemplatesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid templateId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.DeleteGroupTemplateAsync(templateId, cancellationToken);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }

        MyTemplates = await _apiClient.GetMyGroupTemplatesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateGroupAsync(Guid templateId, string? groupName, CancellationToken cancellationToken)
    {
        try
        {
            var group = await _apiClient.CreateGroupFromTemplateAsync(templateId, new CreateGroupFromTemplateRequest(groupName), cancellationToken);
            return RedirectToPage("/Groups/Details", new { id = group.Id });
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            MyTemplates = await _apiClient.GetMyGroupTemplatesAsync(cancellationToken);
            return Page();
        }
    }

    private static List<string> BuildMemberNames(string? text) =>
        (text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n.Length > 0)
            .ToList();
}
