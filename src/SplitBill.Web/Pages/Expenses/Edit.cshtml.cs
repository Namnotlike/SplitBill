using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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

        public List<ItemInput> Items { get; set; } = new();
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

        var splitConfig = ExpenseFormHelpers.BuildSplitConfig(Input.SplitMode, Input.Rows, Input.Items, out var configError);
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
    /// Khôi phục trọng số/%/số tiền nhập tay/danh sách món ăn GỐC từ <see cref="ExpenseDto.SplitConfigJson"/>
    /// (JSON của <see cref="SplitConfigInput"/> — CLAUDE.md mục 4.1) khi có, thay vì chỉ suy ngược từ
    /// <c>ExpenseSplit.Amount</c> cuối cùng như trước đây. Trước 2026-09-05, API chưa từng trả field
    /// này ra dù DB đã lưu sẵn — form Edit luôn phải đoán lại từ Amount (không chính xác khi có làm
    /// tròn, và với Itemized thì hoàn toàn không đoán được nên luôn trống). Vẫn giữ nhánh suy ngược từ
    /// Amount làm fallback phòng khi SplitConfigJson null/parse lỗi (dữ liệu cũ trước bản sửa, hoặc
    /// trường hợp bất thường khác) để form Edit không bao giờ hỏng hoàn toàn.
    /// </summary>
    private static ExpenseInput MapToInput(ExpenseDto expense, GroupDto group)
    {
        var splitConfig = TryParseSplitConfig(expense.SplitConfigJson);
        var splitByMember = expense.Splits.ToDictionary(s => s.MemberId, s => s.Amount);

        var equalMemberIds = splitConfig?.MemberIds?.ToHashSet();
        var shareWeights = splitConfig?.Shares?.ToDictionary(s => s.MemberId, s => s.Weight);
        var percentages = splitConfig?.Percentages?.ToDictionary(p => p.MemberId, p => p.Percent);
        var exactAmounts = splitConfig?.ExactAmounts?.ToDictionary(e => e.MemberId, e => e.Amount);

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
                    EqualParticipant = expense.SplitMode == "Equal" &&
                        (equalMemberIds is not null ? equalMemberIds.Contains(memberId) : isParticipant),
                    ShareWeight = expense.SplitMode == "Shares"
                        ? LookupDecimal(shareWeights, memberId) ?? (isParticipant ? splitAmount : null)
                        : null,
                    Percentage = expense.SplitMode == "Percentage"
                        ? LookupDecimal(percentages, memberId) ?? (isParticipant && expense.TotalAmount > 0
                            ? Math.Round(splitAmount * 100m / expense.TotalAmount, 2)
                            : null)
                        : null,
                    ExactAmount = expense.SplitMode == "ExactAmount"
                        ? LookupLong(exactAmounts, memberId) ?? (isParticipant ? splitAmount : null)
                        : null,
                };
            })
            .ToList();

        var items = splitConfig?.Items?.Select(i => new ItemInput
        {
            Name = i.Name,
            Price = i.Price,
            ConsumerMemberIds = i.ConsumerMemberIds.ToList(),
        }).ToList() ?? new List<ItemInput>();

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
            Items = items,
        };
    }

    private static SplitConfigInput? TryParseSplitConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SplitConfigInput>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? LookupDecimal(Dictionary<Guid, decimal>? dict, Guid memberId) =>
        dict is not null && dict.TryGetValue(memberId, out var value) ? value : null;

    private static long? LookupLong(Dictionary<Guid, long>? dict, Guid memberId) =>
        dict is not null && dict.TryGetValue(memberId, out var value) ? value : null;
}
