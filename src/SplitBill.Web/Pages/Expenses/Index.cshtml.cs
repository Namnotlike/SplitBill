using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Expenses;

public class IndexModel : PageModel
{
    private const int PageSize = 20;

    private readonly SplitBillApiClient _apiClient;

    public IndexModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty(SupportsGet = true)]
    public Guid GroupId { get; set; }

    // ===== Tìm kiếm/lọc khoản chi (CLAUDE.md mục 15.2) — bổ sung 2026-09-05. Mọi field đều
    // SupportsGet=true để form lọc dùng method="get": kết quả có thể bookmark/chia sẻ link, và nút
    // "Trang sau/trước" chỉ cần render lại đúng các asp-route-* hiện có, không cần giữ state riêng. =====
    // Đặt tên property là "PageNumber" (không phải "Page") để không che khuất method
    // PageModel.Page() có sẵn — dùng Name="Page" để giữ nguyên key query string cũ (không phá
    // link filter đã bookmark/chia sẻ trước đây).
    [BindProperty(SupportsGet = true, Name = "Page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Title { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? PayerMemberId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? MinAmount { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? MaxAmount { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    public GroupDto Group { get; set; } = null!;
    public PagedResult<ExpenseDto> Expenses { get; set; } = null!;
    public string? ErrorMessage { get; set; }

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(Title) || PayerMemberId is not null || FromDate is not null
        || ToDate is not null || MinAmount is not null || MaxAmount is not null || !string.IsNullOrWhiteSpace(Category);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(GroupId, cancellationToken);

            // DateOnly -> DateTimeOffset dùng offset local giống lúc lưu OccurredAt (CreateModel/
            // EditModel dùng TimeZoneInfo.Local), để so sánh khớp với dữ liệu đã lưu: FromDate lấy
            // đầu ngày, ToDate lấy cuối ngày (23:59:59.999) để bao trọn cả ngày được chọn.
            var fromDate = FromDate is { } from ? ToLocalStartOfDay(from) : (DateTimeOffset?)null;
            var toDate = ToDate is { } to ? ToLocalEndOfDay(to) : (DateTimeOffset?)null;
            var filter = new ExpenseFilter(Title, PayerMemberId, fromDate, toDate, MinAmount, MaxAmount, Category);

            Expenses = await _apiClient.GetExpensesAsync(GroupId, PageNumber <= 0 ? 1 : PageNumber, PageSize, filter, cancellationToken);
            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid expenseId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.DeleteExpenseAsync(expenseId, cancellationToken);
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Expenses/Index", new { groupId = GroupId });
    }

    public string MemberName(Guid memberId) =>
        Group.Members.FirstOrDefault(m => m.Id == memberId)?.DisplayName ?? "?";

    private static DateTimeOffset ToLocalStartOfDay(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(dateTime, TimeZoneInfo.Local.GetUtcOffset(dateTime));
    }

    private static DateTimeOffset ToLocalEndOfDay(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MaxValue);
        return new DateTimeOffset(dateTime, TimeZoneInfo.Local.GetUtcOffset(dateTime));
    }
}
