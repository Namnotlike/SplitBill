using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public LogoutModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var refreshToken = User.FindFirst(SplitBillClaimTypes.RefreshToken)?.Value;
        if (refreshToken is not null)
        {
            try
            {
                await _apiClient.LogoutAsync(refreshToken, cancellationToken);
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException)
            {
                // Vẫn đăng xuất cookie kể cả khi gọi API thất bại (token đã hết hạn, hoặc Api không
                // phản hồi được — HttpRequestException trước đây không bị bắt ở đây, nghĩa là người
                // dùng không đăng xuất được cục bộ nếu Api sập, cùng lớp bug đã sửa ở Index.cshtml.cs,
                // CLAUDE.md mục 23.4).
            }
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }
}
