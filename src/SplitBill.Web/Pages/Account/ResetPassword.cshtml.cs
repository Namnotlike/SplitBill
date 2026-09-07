using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Auth;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

/// <summary>CLAUDE.md mục 16 — Quên mật khẩu (bổ sung 2026-09-07). Trang đích của link trong email
/// đặt lại mật khẩu (<c>?token=...</c>).</summary>
public class ResetPasswordModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public ResetPasswordModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public bool Succeeded { get; set; }

    public sealed class InputModel
    {
        [Required, MinLength(8, ErrorMessage = "Mật khẩu phải từ 8 ký tự.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required, Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            ErrorMessage = "Thiếu token đặt lại mật khẩu. Vui lòng dùng đúng link trong email.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            ErrorMessage = "Thiếu token đặt lại mật khẩu. Vui lòng dùng đúng link trong email.";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _apiClient.ResetPasswordAsync(new ResetPasswordRequest(Token, Input.NewPassword), cancellationToken);
            Succeeded = true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.ErrorCode == "INVALID_RESET_TOKEN"
                ? "Link đặt lại mật khẩu không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu link mới."
                : ex.Message;
        }

        return Page();
    }
}
