using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Expenses; // PagedResult<T>
using SplitBill.Application.Notifications;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Notifications;

public class IndexModel : PageModel
{
    private const int DefaultPageSize = 20;

    private readonly SplitBillApiClient _apiClient;

    public IndexModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public PagedResult<NotificationDto> Notifications { get; set; } = null!;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Notifications = await _apiClient.GetNotificationsAsync(Math.Max(1, PageNumber), DefaultPageSize, cancellationToken);
    }

    public async Task<IActionResult> OnPostMarkReadAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        await _apiClient.MarkNotificationAsReadAsync(notificationId, cancellationToken);
        return RedirectToPage(new { pageNumber = PageNumber });
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync(CancellationToken cancellationToken)
    {
        await _apiClient.MarkAllNotificationsAsReadAsync(cancellationToken);
        return RedirectToPage(new { pageNumber = PageNumber });
    }
}
