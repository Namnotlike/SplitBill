using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SplitBill.Web.Pages.Account;

/// <summary>Bấm nút "Đăng nhập bằng Google" ở Login/Register sẽ GET trang này — chỉ để kích hoạt
/// Challenge tới Google, không bao giờ tự render (CLAUDE.md mục 25.3).</summary>
public class GoogleLoginModel : PageModel
{
    private readonly IAuthenticationSchemeProvider _schemeProvider;

    public GoogleLoginModel(IAuthenticationSchemeProvider schemeProvider)
    {
        _schemeProvider = schemeProvider;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl)
    {
        // Phòng thủ: nút bấm đã tự ẩn khi chưa cấu hình Google (LoginModel/RegisterModel.
        // GoogleLoginEnabled), nhưng ai đó gõ thẳng URL này vẫn có thể tới đây trước khi Program.cs
        // đăng ký scheme "Google" (chỉ đăng ký khi có ClientId/ClientSecret thật — xem Program.cs) —
        // Challenge() với 1 scheme chưa đăng ký sẽ ném InvalidOperationException, nên kiểm tra trước
        // rồi quay lại trang Login thay vì để sập 500.
        var scheme = await _schemeProvider.GetSchemeAsync(GoogleDefaults.AuthenticationScheme);
        if (scheme is null)
        {
            return RedirectToPage("/Account/Login", new { returnUrl });
        }

        var redirectUrl = Url.Page("/Account/GoogleCallback", new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }
}
