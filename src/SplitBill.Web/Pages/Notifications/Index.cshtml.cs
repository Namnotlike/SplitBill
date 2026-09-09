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

    // Web Push (CLAUDE.md mục 25.7) — rỗng nếu server chưa cấu hình VAPID (tính năng tùy chọn); JS
    // trên trang tự ẩn hẳn khối "Thông báo đẩy" khi rỗng, không hiện nút chắc chắn sẽ lỗi.
    public string VapidPublicKey { get; set; } = string.Empty;

    [BindProperty]
    public PushSubscribeInput PushSubscribe { get; set; } = new();

    // Cố tình KHÔNG dùng [Required] ở đây — bài học từ mục 25.2: [BindProperty] gắn validation
    // attribute trên form A luôn được áp dụng cho MỌI POST của page, kể cả khi form B (MarkRead/
    // MarkAllRead/UnsubscribePush) được submit và PushSubscribe.* vẫn ở giá trị mặc định rỗng. Validate
    // thủ công ngay trong OnPostSubscribePushAsync thay vì dựa vào ModelState.IsValid toàn trang.
    public sealed class PushSubscribeInput
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dhKey { get; set; } = string.Empty;
        public string AuthKey { get; set; } = string.Empty;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Notifications = await _apiClient.GetNotificationsAsync(Math.Max(1, PageNumber), DefaultPageSize, cancellationToken);

        try
        {
            VapidPublicKey = (await _apiClient.GetVapidPublicKeyAsync(cancellationToken)).PublicKey;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            // Cùng nguyên tắc chịu lỗi im lặng như widget cá nhân ở trang chủ (mục 23.4) — lỗi gọi Api
            // chỉ ẩn khối "Thông báo đẩy", không chặn phần còn lại của trang.
        }
    }

    public async Task<IActionResult> OnPostSubscribePushAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(PushSubscribe.Endpoint) || string.IsNullOrWhiteSpace(PushSubscribe.P256dhKey) || string.IsNullOrWhiteSpace(PushSubscribe.AuthKey))
        {
            return RedirectToPage(new { pageNumber = PageNumber });
        }

        await _apiClient.SubscribeToPushAsync(
            new CreatePushSubscriptionRequest(PushSubscribe.Endpoint, PushSubscribe.P256dhKey, PushSubscribe.AuthKey), cancellationToken);
        return RedirectToPage(new { pageNumber = PageNumber });
    }

    public async Task<IActionResult> OnPostUnsubscribePushAsync(string endpoint, CancellationToken cancellationToken)
    {
        await _apiClient.UnsubscribeFromPushAsync(endpoint, cancellationToken);
        return RedirectToPage(new { pageNumber = PageNumber });
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
