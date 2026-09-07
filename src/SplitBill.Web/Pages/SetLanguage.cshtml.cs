using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SplitBill.Web.Pages;

/// <summary>Đổi ngôn ngữ hiện tại (CLAUDE.md mục 22) — không có view riêng, chỉ set cookie
/// <see cref="CookieRequestCultureProvider"/> rồi quay lại đúng trang đang đứng. GET (không phải
/// POST) vì đây không phải hành động ghi dữ liệu, chỉ đổi sở thích hiển thị của TRÌNH DUYỆT — cùng
/// tinh thần với việc đổi dark mode chỉ dùng localStorage phía client, không cần CSRF token.</summary>
public sealed class SetLanguageModel : PageModel
{
    public IActionResult OnGet(string culture, string? returnUrl)
    {
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

        // Chỉ chấp nhận đường dẫn tương đối nội bộ (Url.IsLocalUrl) — chặn open-redirect nếu
        // returnUrl bị chỉnh sửa thành 1 URL bên ngoài.
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
