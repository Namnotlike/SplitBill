using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Auth;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

/// <summary>Bước 2 của đăng nhập khi tài khoản bật 2FA (CLAUDE.md mục 25.9) — Login/GoogleCallback
/// KHÔNG truyền challenge token qua query string (tránh lộ qua URL/lịch sử trình duyệt/referrer) mà
/// qua TempData (cookie-backed, 1 lần chuyển tiếp giữa 2 request). Dùng <c>Peek</c> xuyên suốt (không
/// phải indexer) để giá trị KHÔNG tự bị đánh dấu xóa — người dùng gõ sai mã vẫn thử lại được nhiều lần
/// trên cùng trang mà không phải quay lại bước 1.</summary>
public class TwoFactorChallengeModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public TwoFactorChallengeModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    [Required]
    public string Code { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        if (GetChallengeToken() is null)
        {
            // TempData rỗng — người dùng vào thẳng trang này (bookmark/reload sau khi đã dùng xong)
            // mà không qua bước 1, không có gì để hoàn tất cả.
            return RedirectToPage("/Account/Login");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var challengeToken = GetChallengeToken();
        if (challengeToken is null)
        {
            return RedirectToPage("/Account/Login");
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            ErrorMessage = "Vui lòng nhập mã xác thực.";
            return Page();
        }

        var fallbackName = TempData.Peek("TwoFactorFallbackName") as string ?? "";
        var returnUrl = TempData.Peek("TwoFactorReturnUrl") as string;

        try
        {
            var tokens = await _apiClient.CompleteTwoFactorLoginAsync(new CompleteTwoFactorLoginRequest(challengeToken, Code.Trim()), cancellationToken);
            TempData.Remove("TwoFactorChallengeToken");
            TempData.Remove("TwoFactorFallbackName");
            TempData.Remove("TwoFactorReturnUrl");
            await SignInHelper.SignInAsync(HttpContext, _apiClient, tokens, fallbackName, cancellationToken);
            return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/Groups/Index" : returnUrl);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.StatusCode == 401 ? "Mã xác thực không đúng hoặc yêu cầu đã hết hạn." : ex.Message;
            return Page();
        }
    }

    private string? GetChallengeToken() => TempData.Peek("TwoFactorChallengeToken") as string;
}
