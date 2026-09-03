using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Expenses;

public class CreateModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public CreateModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty(SupportsGet = true)]
    public Guid GroupId { get; set; }

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
        public DateTime OccurredAt { get; set; } = DateTime.Now;

        public string? Note { get; set; }

        [Required]
        public string SplitMode { get; set; } = "Equal";

        public List<MemberRowInput> Rows { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(GroupId, cancellationToken);
            Input.Rows = Group.Members.Select(m => new MemberRowInput { MemberId = m.Id, EqualParticipant = true }).ToList();
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
        Group = await _apiClient.GetGroupAsync(GroupId, cancellationToken);

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
            var result = await _apiClient.CreateExpenseAsync(
                GroupId,
                new CreateExpenseRequest(Input.Title, Input.TotalAmount, Input.ExtraFeeAmount, occurredAt, payers, Input.SplitMode, splitConfig!, Input.Note),
                cancellationToken);

            if (result.Warnings.Count > 0)
            {
                TempData["SuccessMessage"] = "Đã lưu, nhưng có cảnh báo: " + string.Join(" ", result.Warnings.Select(w => w.Message));
            }
            else
            {
                TempData["SuccessMessage"] = "Đã thêm khoản chi.";
            }

            return RedirectToPage("/Expenses/Index", new { groupId = GroupId });
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }
}
