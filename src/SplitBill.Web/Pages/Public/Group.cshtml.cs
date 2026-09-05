using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Public;

// Trang này cho phép xem ẩn danh (đúng đặc tả GET /groups/shared/{shareToken} — read-only, không cần
// đăng nhập). [AllowAnonymous] chỉ áp dụng cho GET; OnPostJoinAsync bên dưới tự kiểm tra đăng nhập
// riêng (CLAUDE.md mục 15.6 — tham gia nhóm YÊU CẦU tài khoản), không dựa vào [Authorize] ở class vì
// điều đó sẽ chặn luôn cả GET xem ẩn danh.
[AllowAnonymous]
public class GroupModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public GroupModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GroupDto? Group { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(string shareToken, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetSharedGroupAsync(shareToken, cancellationToken);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task<IActionResult> OnPostJoinAsync(string shareToken, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            // Không thể xảy ra qua UI bình thường (nút Tham gia chỉ hiện khi đã đăng nhập — xem
            // Group.cshtml), nhưng vẫn phòng vệ nếu ai đó POST thẳng vào handler này.
            return RedirectToPage("/Account/Login", new { returnUrl = $"/Public/Group/{shareToken}" });
        }

        try
        {
            var member = await _apiClient.JoinGroupAsync(shareToken, cancellationToken);
            var group = await _apiClient.GetSharedGroupAsync(shareToken, cancellationToken);
            TempData["SuccessMessage"] = $"Bạn đã tham gia nhóm \"{group.Name}\" với tên {member.DisplayName}.";
            return RedirectToPage("/Groups/Details", new { id = group.Id });
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            Group = await _apiClient.GetSharedGroupAsync(shareToken, cancellationToken);
            return Page();
        }
    }
}
