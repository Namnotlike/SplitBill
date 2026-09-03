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
            return RedirectToPage("/Groups/Index");
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
