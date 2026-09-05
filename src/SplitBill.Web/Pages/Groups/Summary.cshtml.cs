using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

/// <summary>Xuất PDF tổng kết chuyến đi (CLAUDE.md mục 15.9) — trang HTML in được qua trình duyệt
/// (window.print() + CSS @media print), KHÔNG thêm thư viện sinh PDF mới (CLAUDE.md mục 2: không
/// thêm NuGet ngoài danh sách nếu chưa hỏi người dùng).</summary>
public class SummaryModel : PageModel
{
    private const int PageSize = 100; // giới hạn tối đa 1 trang của API (ExpensesController)

    private readonly SplitBillApiClient _apiClient;

    public SummaryModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GroupDto Group { get; set; } = null!;
    public IReadOnlyList<ExpenseDto> Expenses { get; set; } = Array.Empty<ExpenseDto>();
    public IReadOnlyList<MemberBalanceDto> Balances { get; set; } = Array.Empty<MemberBalanceDto>();
    public SettlementPlanDto Plan { get; set; } = null!;
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Tổng chi tiêu theo danh mục — thống kê đơn giản đầu tiên tận dụng nhãn/danh mục
    /// khoản chi (CLAUDE.md mục 15.3 "paving the way for future spending stats").</summary>
    public IReadOnlyList<(string Category, long Total)> CategoryTotals { get; set; } = Array.Empty<(string, long)>();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(id, cancellationToken);

            // Gộp toàn bộ trang (API giới hạn tối đa 100/trang) — hầu hết nhóm chỉ 1 trang, nhưng vẫn
            // duyệt hết để bản tổng kết không bỏ sót khoản chi nào nếu nhóm có > 100 khoản.
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

            CategoryTotals = Expenses
                .GroupBy(e => e.Category)
                .Select(g => (Category: g.Key, Total: g.Sum(e => e.TotalAmount)))
                .OrderByDescending(c => c.Total)
                .ToList();

            Balances = await _apiClient.GetBalancesAsync(id, cancellationToken);
            Plan = await _apiClient.GetSettlementPlanAsync(id, cancellationToken);
            GeneratedAt = DateTimeOffset.Now;

            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }
}
