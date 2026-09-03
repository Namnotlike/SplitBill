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
            catch (ApiException)
            {
                // Vẫn đăng xuất cookie kể cả khi gọi API thất bại (token đã hết hạn chẳng hạn).
            }
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }
}
