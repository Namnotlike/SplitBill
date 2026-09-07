using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

/// <summary>Thống kê chi tiêu theo thời gian + danh mục (CLAUDE.md mục 17) — biểu đồ dùng Chart.js
/// (CDN cdnjs, cùng mẫu qrcodejs đã dùng ở SettlementPlan.cshtml — CLAUDE.md mục 2 chỉ giới hạn
/// NuGet backend, không áp dụng cho thư viện JS phía client).</summary>
public class StatisticsModel : PageModel
{
    private const int PageSize = 100; // giới hạn tối đa 1 trang của API (ExpensesController)

    private readonly SplitBillApiClient _apiClient;

    public StatisticsModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GroupDto Group { get; set; } = null!;
    public IReadOnlyList<ExpenseDto> Expenses { get; set; } = Array.Empty<ExpenseDto>();

    /// <summary>Tổng chi tiêu theo tháng (nhãn "MM/yyyy"), sắp theo thời gian tăng dần — dùng cho
    /// biểu đồ cột "Chi tiêu theo thời gian". Chỉ tính <c>TotalAmount</c> (không cộng ExtraFeeAmount),
    /// cùng quy ước với <see cref="SummaryModel.CategoryTotals"/> (mục 15.9) để 2 trang thống kê không
    /// lệch số nhau nếu người dùng đối chiếu.</summary>
    public IReadOnlyList<(string MonthLabel, long Total)> MonthlyTotals { get; set; } = Array.Empty<(string, long)>();

    public IReadOnlyList<(string Category, string Label, string Icon, long Total)> CategoryTotals { get; set; } =
        Array.Empty<(string, string, string, long)>();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(id, cancellationToken);

            // Gộp toàn bộ trang (API giới hạn tối đa 100/trang) — cùng mẫu SummaryModel (mục 15.9),
            // để biểu đồ không bỏ sót khoản chi nào nếu nhóm có > 100 khoản.
            var allExpenses = new List<ExpenseDto>();
            var page = 1;
            while (true)
            {
                var result = await _apiClient.GetExpensesAsync(id, page, PageSize, ExpenseFilter.Empty, cancellationToken);
                allExpenses.AddRange(result.Items);
                if (allExpenses.Count >= result.TotalCount || result.Items.Count == 0)
                {
                    break;
                }
                page++;
            }
            Expenses = allExpenses.OrderBy(e => e.OccurredAt).ToList();

            MonthlyTotals = Expenses
                .GroupBy(e => new DateOnly(e.OccurredAt.Year, e.OccurredAt.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => (g.Key.ToString("MM/yyyy"), g.Sum(e => e.TotalAmount)))
                .ToList();

            CategoryTotals = Expenses
                .GroupBy(e => e.Category)
                .Select(g =>
                {
                    var info = ExpenseCategoryOptions.Get(g.Key);
                    return (Category: g.Key, info.Label, info.Icon, Total: g.Sum(e => e.TotalAmount));
                })
                .OrderByDescending(c => c.Total)
                .ToList();

            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }
}
