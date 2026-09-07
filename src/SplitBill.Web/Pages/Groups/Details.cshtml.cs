using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

public class DetailsModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public DetailsModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GroupDto Group { get; set; } = null!;

    [BindProperty]
    public AddMemberInput NewMember { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public sealed class AddMemberInput
    {
        [Required]
        public string DisplayName { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(id, cancellationToken);
            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }

    public async Task<IActionResult> OnPostAddMemberAsync(Guid id, CancellationToken cancellationToken)
    {
        Group = await _apiClient.GetGroupAsync(id, cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _apiClient.AddMemberAsync(id, new AddMemberRequest(null, NewMember.DisplayName), cancellationToken);
            return RedirectToPage("/Groups/Details", new { id });
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.RemoveMemberAsync(id, memberId, cancellationToken);
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Groups/Details", new { id });
    }

    public async Task<IActionResult> OnPostToggleSimplifyAsync(Guid id, bool simplifyDebts, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.UpdateGroupAsync(id, new UpdateGroupRequest(null, null, !simplifyDebts, null), cancellationToken);
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Groups/Details", new { id });
    }

    public async Task<IActionResult> OnGetExportExpensesAsync(Guid id, CancellationToken cancellationToken)
    {
        var bytes = await _apiClient.ExportExpensesCsvAsync(id, cancellationToken);
        return File(bytes, "text/csv", $"khoan-chi-{id}.csv");
    }

    public async Task<IActionResult> OnGetExportBalancesAsync(Guid id, CancellationToken cancellationToken)
    {
        var bytes = await _apiClient.ExportBalancesCsvAsync(id, cancellationToken);
        return File(bytes, "text/csv", $"so-du-{id}.csv");
    }

    // CLAUDE.md mục 18 — Nhân bản nhóm: 1-click, luôn dùng tên tự sinh "{Tên gốc} (bản sao)" (không có
    // form nhập tên riêng — người dùng đổi tên sau ở trang Sửa nhóm nếu muốn, giữ hành động này đơn
    // giản đúng tinh thần "1 nút bấm" như RotateShareToken).
    public async Task<IActionResult> OnPostDuplicateAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var duplicated = await _apiClient.DuplicateGroupAsync(id, new DuplicateGroupRequest(null), cancellationToken);
            TempData["SuccessMessage"] = $"Đã nhân bản thành nhóm mới \"{duplicated.Name}\".";
            return RedirectToPage("/Groups/Details", new { id = duplicated.Id });
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Details", new { id });
        }
    }

    public async Task<IActionResult> OnPostRotateShareTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.RotateShareTokenAsync(id, cancellationToken);
            TempData["SuccessMessage"] = "Đã đổi link chia sẻ.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Groups/Details", new { id });
    }
}
