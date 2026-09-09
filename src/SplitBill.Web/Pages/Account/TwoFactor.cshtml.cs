using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Auth;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Account;

/// <summary>Trang cài đặt Xác thực 2 lớp (CLAUDE.md mục 25.9). 3 ô nhập mã (Enable/Disable/Regenerate)
/// CỐ TÌNH là `string` thuần, KHÔNG gắn `[Required]` — bài học từ mục 25.2: `[BindProperty]` áp dụng
/// validation cho MỌI POST của trang bất kể handler nào chạy, nên validate thủ công trong từng handler
/// thay vì dựa vào `ModelState.IsValid` toàn trang.</summary>
[Authorize]
public class TwoFactorModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public TwoFactorModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public bool Enabled { get; set; }
    public string? PendingSecret { get; set; }
    public string? PendingOtpAuthUri { get; set; }

    /// <summary>Chỉ khác null NGAY SAU KHI bật/sinh lại mã dự phòng thành công — hiển thị ĐÚNG 1 LẦN,
    /// DB chỉ lưu hash nên không bao giờ đọc lại được sau lần này.</summary>
    public IReadOnlyList<string>? RecoveryCodesToShow { get; set; }

    [BindProperty]
    public string EnableCode { get; set; } = string.Empty;

    [BindProperty]
    public string DisableCode { get; set; } = string.Empty;

    [BindProperty]
    public string RegenerateCode { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await ReloadPageStateAsync(cancellationToken);

    public async Task<IActionResult> OnPostStartSetupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var setup = await _apiClient.SetupTwoFactorAsync(cancellationToken);
            // Giữ lại qua TempData để hiển thị xuyên suốt các lượt thử xác nhận (kể cả gõ sai mã) mà
            // không phải gọi lại /2fa/setup — gọi lại sẽ sinh secret MỚI và vô hiệu QR vừa quét.
            TempData["TwoFactorPendingSecret"] = setup.SecretBase32;
            TempData["TwoFactorPendingOtpAuthUri"] = setup.OtpAuthUri;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public IActionResult OnPostCancelSetup()
    {
        TempData.Remove("TwoFactorPendingSecret");
        TempData.Remove("TwoFactorPendingOtpAuthUri");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostConfirmEnableAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EnableCode))
        {
            ErrorMessage = "Vui lòng nhập mã xác thực.";
            await ReloadPageStateAsync(cancellationToken);
            return Page();
        }

        try
        {
            var result = await _apiClient.EnableTwoFactorAsync(new EnableTwoFactorRequest(EnableCode.Trim()), cancellationToken);
            TempData.Remove("TwoFactorPendingSecret");
            TempData.Remove("TwoFactorPendingOtpAuthUri");
            RecoveryCodesToShow = result.RecoveryCodes;
            SuccessMessage = "Đã bật xác thực 2 lớp. LƯU LẠI các mã dự phòng bên dưới ngay — chỉ hiện đúng 1 lần này.";
            Enabled = true;
            return Page();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.StatusCode == 401 ? "Mã xác thực không đúng." : ex.Message;
            await ReloadPageStateAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDisableAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(DisableCode))
        {
            ErrorMessage = "Vui lòng nhập mã xác thực hoặc mã dự phòng.";
            await ReloadPageStateAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _apiClient.DisableTwoFactorAsync(new DisableTwoFactorRequest(DisableCode.Trim()), cancellationToken);
            SuccessMessage = "Đã tắt xác thực 2 lớp.";
            Enabled = false;
            return Page();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.StatusCode == 401 ? "Mã xác thực không đúng." : ex.Message;
            await ReloadPageStateAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRegenerateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(RegenerateCode))
        {
            ErrorMessage = "Vui lòng nhập mã xác thực.";
            await ReloadPageStateAsync(cancellationToken);
            return Page();
        }

        try
        {
            var result = await _apiClient.RegenerateTwoFactorRecoveryCodesAsync(new RegenerateRecoveryCodesRequest(RegenerateCode.Trim()), cancellationToken);
            RecoveryCodesToShow = result.RecoveryCodes;
            SuccessMessage = "Đã sinh bộ mã dự phòng mới — bộ cũ không còn dùng được. LƯU LẠI ngay, chỉ hiện đúng 1 lần này.";
            Enabled = true;
            return Page();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.StatusCode == 401 ? "Mã xác thực không đúng." : ex.Message;
            await ReloadPageStateAsync(cancellationToken);
            return Page();
        }
    }

    private async Task ReloadPageStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            Enabled = (await _apiClient.GetTwoFactorStatusAsync(cancellationToken)).Enabled;
        }
        catch (ApiException)
        {
            // Giữ mặc định false — trang vẫn hiển thị được, chỉ là không biết chắc trạng thái hiện tại.
        }

        PendingSecret = TempData.Peek("TwoFactorPendingSecret") as string;
        PendingOtpAuthUri = TempData.Peek("TwoFactorPendingOtpAuthUri") as string;
    }
}
