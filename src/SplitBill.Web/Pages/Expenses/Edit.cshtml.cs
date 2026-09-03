using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Expenses;

public class EditModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public EditModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty(SupportsGet = true)]
    public Guid ExpenseId { get; set; }

    public GroupDto Group { get; set; } = null!;

    [BindProperty]
    public ExpenseInput Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public sealed class ExpenseInput
    {
        [Required]
        public string Title { get; set; } = string.Empty;

        [Range(1, long.MaxValue, ErrorMessage = "Tổng tiền phải > 0")]
        public long TotalAmount { get; set; }

        [Range(0, long.MaxValue)]
        public long ExtraFeeAmount { get; set; }

        [Required]
        public DateTime OccurredAt { get; set; }

        public string? Note { get; set; }

        [Required]
        public string SplitMode { get; set; } = "Equal";

        [Required]
        public string RowVersion { get; set; } = string.Empty;

        public List<MemberRowInput> Rows { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var expense = await _apiClient.GetExpenseAsync(ExpenseId, cancellationToken);
            Group = await _apiClient.GetGroupAsync(expense.GroupId, cancellationToken);
            Input = MapToInput(expense, Group);
            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var existing = await _apiClient.GetExpenseAsync(ExpenseId, cancellationToken);
        Group = await _apiClient.GetGroupAsync(existing.GroupId, cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var payers = Input.Rows
            .Where(r => r.PayerAmount is > 0)
            .Select(r => new ExpensePayerInput(r.MemberId, r.PayerAmount!.Value))
            .ToList();

        if (payers.Count == 0)
        {
            ErrorMessage = "Cần ít nhất 1 người ứng tiền (Amount > 0).";
            return Page();
        }

        var splitConfig = ExpenseFormHelpers.BuildSplitConfig(Input.SplitMode, Input.Rows, out var configError);
        if (configError is not null)
        {
            ErrorMessage = configError;
            return Page();
        }

        var occurredAt = new DateTimeOffset(Input.OccurredAt, TimeZoneInfo.Local.GetUtcOffset(Input.OccurredAt));

        try
        {
            var result = await _apiClient.UpdateExpenseAsync(
                ExpenseId,
                new UpdateExpenseRequest(Input.Title, Input.TotalAmount, Input.ExtraFeeAmount, occurredAt, payers, Input.SplitMode, splitConfig!, Input.RowVersion, Input.Note),
                cancellationToken);

            TempData["SuccessMessage"] = result.Warnings.Count > 0
                ? "Đã lưu, nhưng có cảnh báo: " + string.Join(" ", result.Warnings.Select(w => w.Message))
                : "Đã cập nhật khoản chi.";

            return RedirectToPage("/Expenses/Index", new { groupId = existing.GroupId });
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.StatusCode == 409
                ? "Khoản chi đã bị người khác sửa trước đó. Vui lòng tải lại trang và thử lại."
                : ex.Message;
            return Page();
        }
    }

    /// <summary>
    /// API chỉ lưu số tiền chia cuối cùng (ExpenseSplit.Amount), không lưu trọng số/% gốc người
    /// dùng đã nhập (CLAUDE.md mục 4.1: SplitConfigJson lưu input gốc dạng JSON tự do, không có
    /// contract đọc lại có cấu trúc) — nên form Edit dùng chính Amount hiện tại làm trọng số/% khởi
    /// tạo. Nếu người dùng không đổi gì, kết quả tính lại sẽ giữ nguyên tỉ lệ cũ (vì Shares chỉ quan
    /// tâm tỉ lệ tương đối). Nếu đổi TotalAmount thì các giá trị này chỉ là điểm khởi đầu gần đúng.
    /// </summary>
    private static ExpenseInput MapToInput(ExpenseDto expense, GroupDto group)
    {
        var splitByMember = expense.Splits.ToDictionary(s => s.MemberId, s => s.Amount);

        // Duyệt TOÀN BỘ thành viên nhóm (không chỉ người đã có mặt trong expense) để form Edit vẫn
        // cho phép thêm payer/người tham gia mới.
        var rows = group.Members
            .Select(m => m.Id)
            .Select(memberId =>
            {
                var payerAmount = expense.Payers.FirstOrDefault(p => p.MemberId == memberId)?.Amount;
                var splitAmount = splitByMember.GetValueOrDefault(memberId);
                var isParticipant = splitByMember.ContainsKey(memberId);

                return new MemberRowInput
                {
                    MemberId = memberId,
                    PayerAmount = payerAmount,
                    EqualParticipant = expense.SplitMode == "Equal" && isParticipant,
                    ShareWeight = expense.SplitMode == "Shares" && isParticipant ? splitAmount : null,
                    Percentage = expense.SplitMode == "Percentage" && isParticipant && expense.TotalAmount > 0
                        ? Math.Round(splitAmount * 100m / expense.TotalAmount, 2)
                        : null,
                    ExactAmount = expense.SplitMode == "ExactAmount" && isParticipant ? splitAmount : null,
                };
            })
            .ToList();

        return new ExpenseInput
        {
            Title = expense.Title,
            TotalAmount = expense.TotalAmount,
            ExtraFeeAmount = expense.ExtraFeeAmount,
            OccurredAt = expense.OccurredAt.LocalDateTime,
            Note = expense.Note,
            SplitMode = expense.SplitMode,
            RowVersion = expense.RowVersion,
            Rows = rows,
        };
    }
}
