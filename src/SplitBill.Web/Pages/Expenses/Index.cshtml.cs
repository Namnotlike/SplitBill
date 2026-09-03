using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Expenses;

public class IndexModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public IndexModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty(SupportsGet = true)]
    public Guid GroupId { get; set; }

    public GroupDto Group { get; set; } = null!;
    public PagedResult<ExpenseDto> Expenses { get; set; } = null!;
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int page, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(GroupId, cancellationToken);
            Expenses = await _apiClient.GetExpensesAsync(GroupId, page <= 0 ? 1 : page, 20, cancellationToken);
            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid expenseId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.DeleteExpenseAsync(expenseId, cancellationToken);
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Expenses/Index", new { groupId = GroupId });
    }

    public string MemberName(Guid memberId) =>
        Group.Members.FirstOrDefault(m => m.Id == memberId)?.DisplayName ?? "?";
}
