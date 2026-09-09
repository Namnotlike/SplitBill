using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

/// <summary>Khôi phục khoản chi/thanh toán đã xóa (CLAUDE.md mục 24) — soft-delete (<c>IsDeleted</c>)
/// đã có sẵn từ M1 (mục 1 "Không xóa cứng dữ liệu tài chính") nhưng trước tính năng này không có cách
/// nào xem lại/khôi phục qua UI: dữ liệu vẫn còn nguyên trong DB nhưng "biến mất" khỏi mọi trang, xóa
/// nhầm 1 khoản chi/thanh toán trước đây là vĩnh viễn về mặt thao tác dù vẫn phục hồi được thủ công
/// qua DB.</summary>
public class RecentlyDeletedModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public RecentlyDeletedModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GroupDto Group { get; set; } = null!;
    public IReadOnlyList<ExpenseDto> DeletedExpenses { get; set; } = [];
    public IReadOnlyList<SettlementDto> DeletedSettlements { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(id, cancellationToken);
            DeletedExpenses = await _apiClient.GetDeletedExpensesAsync(id, cancellationToken);
            DeletedSettlements = await _apiClient.GetDeletedSettlementsAsync(id, cancellationToken);
            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }

    public async Task<IActionResult> OnPostRestoreExpenseAsync(Guid id, Guid expenseId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.RestoreExpenseAsync(expenseId, cancellationToken);
            TempData["SuccessMessage"] = "Đã khôi phục khoản chi.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRestoreSettlementAsync(Guid id, Guid settlementId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.RestoreSettlementAsync(settlementId, cancellationToken);
            TempData["SuccessMessage"] = "Đã khôi phục thanh toán.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage(new { id });
    }

    public string MemberName(Guid memberId) =>
        Group.Members.FirstOrDefault(m => m.Id == memberId)?.DisplayName ?? "?";
}
