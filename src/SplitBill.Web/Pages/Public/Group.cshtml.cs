using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Public;

[AllowAnonymous]
public class GroupModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public GroupModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GroupDto? Group { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(string shareToken, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetSharedGroupAsync(shareToken, cancellationToken);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
