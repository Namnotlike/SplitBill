using Microsoft.AspNetCore.Mvc;
using SplitBill.Web.Services;

namespace SplitBill.Web.ViewComponents;

/// <summary>Bell + số thông báo chưa đọc trên navbar (CLAUDE.md mục 13) — hiển thị ở mọi trang qua
/// _Layout.cshtml. Lỗi gọi Api (vd hết hạn token giữa lúc render) không được làm sập trang, chỉ ẩn badge.</summary>
public sealed class NotificationBadgeViewComponent : ViewComponent
{
    private readonly SplitBillApiClient _apiClient;

    public NotificationBadgeViewComponent(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal.Identity?.IsAuthenticated != true)
        {
            return Content(string.Empty);
        }

        try
        {
            var result = await _apiClient.GetUnreadNotificationCountAsync(HttpContext.RequestAborted);
            return View(result.Count);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            // Component này chạy trên MỌI trang đã đăng nhập (_Layout.cshtml) — HttpRequestException
            // (Api không phản hồi được) trước đây không bị bắt, nghĩa là Api sập sẽ làm sập TOÀN BỘ
            // trang, không chỉ mỗi badge. Cùng lớp bug đã sửa ở Index.cshtml.cs (CLAUDE.md mục 23.4).
            return Content(string.Empty);
        }
    }
}
