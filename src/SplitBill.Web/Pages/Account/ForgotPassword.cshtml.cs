using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Auth;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

/// <summary>CLAUDE.md mục 16 — Quên mật khẩu (bổ sung 2026-09-07).</summary>
public class ForgotPasswordModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public ForgotPasswordModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Submitted { get; set; }

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
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

        // API luôn trả 204 dù email có tồn tại hay không (CLAUDE.md mục 8) — Web chỉ việc hiện thông
        // báo chung chung, không cần (và không được) phân biệt các trường hợp.
        try
        {
            await _apiClient.ForgotPasswordAsync(new ForgotPasswordRequest(Input.Email), cancellationToken);
        }
        catch (ApiException)
        {
            // Kể cả lỗi (vd validation email sai định dạng phía API) cũng không tiết lộ gì thêm —
            // hiện đúng thông báo chung như thành công, tránh oracle cho việc dò email tồn tại.
        }

        Submitted = true;
        return Page();
    }
}
