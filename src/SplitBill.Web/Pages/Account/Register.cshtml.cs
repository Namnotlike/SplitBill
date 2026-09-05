using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Auth;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public RegisterModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Cho phép quay lại đúng trang trước đó sau khi đăng ký — cụ thể dùng để "đăng ký rồi tham gia
    // nhóm qua link chia sẻ" (CLAUDE.md mục 15.6), cùng mẫu ReturnUrl đã có sẵn ở LoginModel.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8, ErrorMessage = "Mật khẩu phải từ 8 ký tự.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string DisplayName { get; set; } = string.Empty;
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
            var tokens = await _apiClient.RegisterAsync(
                new RegisterRequest(Input.Email, Input.Password, Input.DisplayName), cancellationToken);
            await SignInHelper.SignInAsync(HttpContext, _apiClient, tokens, Input.DisplayName, cancellationToken);
            return string.IsNullOrEmpty(ReturnUrl) ? RedirectToPage("/Groups/Index") : LocalRedirect(ReturnUrl);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
