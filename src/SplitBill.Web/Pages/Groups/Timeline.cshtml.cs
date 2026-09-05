using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

/// <summary>Timeline hoạt động nhóm (CLAUDE.md mục 15.5) — gộp Expense/Settlement/GroupMember/Group
/// thành 1 dòng thời gian duy nhất. Không tự truy vấn 3 nguồn dữ liệu riêng: AuditLog (đã ghi nhận
/// mọi thay đổi từ trước) chính là nguồn hợp nhất sẵn có, trang này chỉ hiển thị lại
/// <see cref="AuditLogDto.Summary"/> đã được Application dựng sẵn thành câu tiếng Việt.</summary>
public class TimelineModel : PageModel
{
    private const int PageSize = 20;

    private readonly SplitBillApiClient _apiClient;

    public TimelineModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GroupDto Group { get; set; } = null!;
    public PagedResult<AuditLogDto> Logs { get; set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, int page, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(id, cancellationToken);
            Logs = await _apiClient.GetAuditLogsAsync(id, page <= 0 ? 1 : page, PageSize, cancellationToken);
            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }

    public static string IconFor(string entityType) => entityType switch
    {
        "Expense" => "🧾",
        "Settlement" => "💸",
        "GroupMember" => "👤",
        "Group" => "⚙️",
        _ => "•",
    };
}
