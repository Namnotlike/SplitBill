using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Common;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

public class IndexModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public IndexModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public IReadOnlyList<GroupSummaryDto> MyGroups { get; set; } = Array.Empty<GroupSummaryDto>();

    [BindProperty]
    public CreateGroupInput NewGroup { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public sealed class CreateGroupInput
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public string Type { get; set; } = "OneTime";

        [Required]
        public string Currency { get; set; } = SupportedCurrencies.Default;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        MyGroups = await _apiClient.GetMyGroupsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            MyGroups = await _apiClient.GetMyGroupsAsync(cancellationToken);
            return Page();
        }

        try
        {
            var group = await _apiClient.CreateGroupAsync(
                new CreateGroupRequest(NewGroup.Name, NewGroup.Description, NewGroup.Type, NewGroup.Currency), cancellationToken);
            return RedirectToPage("/Groups/Details", new { id = group.Id });
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            MyGroups = await _apiClient.GetMyGroupsAsync(cancellationToken);
            return Page();
        }
    }
}
