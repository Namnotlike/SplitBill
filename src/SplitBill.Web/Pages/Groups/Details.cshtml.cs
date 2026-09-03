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
