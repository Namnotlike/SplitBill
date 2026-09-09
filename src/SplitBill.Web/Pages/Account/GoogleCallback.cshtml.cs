using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Auth;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

/// <summary>Google redirect về đúng CallbackPath của middleware (mặc định /signin-google), middleware
/// tự xử lý xong OAuth rồi mới redirect tiếp sang trang NÀY (RedirectUri đã đặt lúc Challenge ở
/// GoogleLoginModel) — tại đây claim Google (đã xác thực) nằm trong cookie "External" tạm, đọc ra rồi
/// gọi Api để đổi thành tài khoản/JWT thật (CLAUDE.md mục 25.3).</summary>
public class GoogleCallbackModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public GoogleCallbackModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        AuthenticateResult result;
        try
        {
            result = await HttpContext.AuthenticateAsync("External");
        }
        catch (InvalidOperationException)
        {
            // Scheme "External" chỉ được đăng ký khi Google đã cấu hình (xem Program.cs) — ai đó gõ
            // thẳng URL này khi chưa cấu hình sẽ rơi vào đây thay vì sập 500.
            ErrorMessage = "Đăng nhập bằng Google chưa được thiết lập. Vui lòng dùng email/mật khẩu.";
            return Page();
        }
        // Luôn dọn cookie tạm ngay sau khi đọc xong, dù thành công hay fail — không để nó sống sót
        // qua request (nó không phải phiên đăng nhập thật của app).
        await HttpContext.SignOutAsync("External");

        if (!result.Succeeded || result.Principal is null)
        {
            ErrorMessage = "Đăng nhập bằng Google không thành công. Vui lòng thử lại.";
            return Page();
        }

        var googleId = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = result.Principal.FindFirstValue(ClaimTypes.Email);
        var displayName = result.Principal.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(googleId) || string.IsNullOrWhiteSpace(email))
        {
            ErrorMessage = "Google không trả về đủ thông tin (email). Vui lòng thử lại hoặc dùng email/mật khẩu.";
            return Page();
        }

        var effectiveDisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName;

        try
        {
            var loginResult = await _apiClient.GoogleLoginAsync(new GoogleLoginRequest(googleId, email, effectiveDisplayName), cancellationToken);
            if (loginResult.RequiresTwoFactor)
            {
                // 2FA áp dụng CẢ cho đăng nhập qua Google (CLAUDE.md mục 25.9) — không bỏ qua, nếu
                // không 2FA sẽ vô nghĩa với người dùng đã liên kết cả 2 cách đăng nhập.
                TempData["TwoFactorChallengeToken"] = loginResult.TwoFactorChallengeToken;
                TempData["TwoFactorFallbackName"] = effectiveDisplayName;
                TempData["TwoFactorReturnUrl"] = returnUrl;
                return RedirectToPage("/Account/TwoFactorChallenge");
            }

            await SignInHelper.SignInAsync(HttpContext, _apiClient, loginResult.Tokens!, effectiveDisplayName, cancellationToken);
            return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/Groups/Index" : returnUrl);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
