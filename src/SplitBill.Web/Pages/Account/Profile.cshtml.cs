using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Users;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public ProfileModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public sealed class InputModel
    {
        [Required]
        public string DisplayName { get; set; } = string.Empty;

        public string? BankAccountNumber { get; set; }

        [MaxLength(6)]
        public string? BankBin { get; set; }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var profile = await _apiClient.GetMeAsync(cancellationToken);
        Input = new InputModel
        {
            DisplayName = profile.DisplayName,
            BankAccountNumber = profile.BankAccountNumber,
            BankBin = profile.BankBin,
        };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _apiClient.UpdateMeAsync(
                new UpdateProfileRequest(Input.DisplayName, Input.BankAccountNumber, Input.BankBin), cancellationToken);
            SuccessMessage = "Đã lưu.";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }

        return Page();
    }
}
