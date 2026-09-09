using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using SplitBill.Application.Auth;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public LoginModel(SplitBillApiClient apiClient, IConfiguration configuration)
    {
        _apiClient = apiClient;
        // Chỉ hiện nút "Đăng nhập bằng Google" khi đã cấu hình ClientId thật (CLAUDE.md mục 25.3) —
        // tránh dẫn người dùng vào 1 luồng chắc chắn lỗi (Google từ chối client_id rỗng).
        GoogleLoginEnabled = !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]);
    }

    public bool GoogleLoginEnabled { get; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var result = await _apiClient.LoginAsync(new LoginRequest(Input.Email, Input.Password), cancellationToken);
            if (result.RequiresTwoFactor)
            {
                // Chuyển tiếp challenge token qua TempData (KHÔNG qua query string — tránh lộ qua
                // URL/lịch sử trình duyệt/referrer, xem TwoFactorChallengeModel).
                TempData["TwoFactorChallengeToken"] = result.TwoFactorChallengeToken;
                TempData["TwoFactorFallbackName"] = Input.Email;
                TempData["TwoFactorReturnUrl"] = ReturnUrl;
                return RedirectToPage("/Account/TwoFactorChallenge");
            }

            await SignInHelper.SignInAsync(HttpContext, _apiClient, result.Tokens!, Input.Email, cancellationToken);
            return LocalRedirect(string.IsNullOrEmpty(ReturnUrl) ? "/Groups/Index" : ReturnUrl);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.StatusCode == 401 ? "Email hoặc mật khẩu không đúng." : ex.Message;
            return Page();
        }
    }
}
