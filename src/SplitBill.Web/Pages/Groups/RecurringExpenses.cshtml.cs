using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Groups;
using SplitBill.Application.RecurringExpenses;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

/// <summary>Khoản chi định kỳ (CLAUDE.md mục 15.7) — chỉ áp dụng cho nhóm loại Recurring.</summary>
public class RecurringExpensesModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public RecurringExpensesModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty(SupportsGet = true)]
    public Guid GroupId { get; set; }

    public GroupDto Group { get; set; } = null!;
    public IReadOnlyList<RecurringExpenseTemplateDto> Templates { get; set; } = Array.Empty<RecurringExpenseTemplateDto>();

    [BindProperty]
    public TemplateInput Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public sealed class TemplateInput
    {
        [Required]
        public string Title { get; set; } = string.Empty;

        [Range(1, long.MaxValue, ErrorMessage = "Tổng tiền phải > 0")]
        public long TotalAmount { get; set; }

        [Range(0, long.MaxValue)]
        public long ExtraFeeAmount { get; set; }

        public string? Note { get; set; }

        [Required]
        public string Category { get; set; } = "Other";

        [Required]
        public string SplitMode { get; set; } = "Equal";

        [Required]
        public string Interval { get; set; } = "Monthly";

        [Required]
        public DateTime FirstRunAt { get; set; } = DateTime.Today.AddDays(1);

        public List<MemberRowInput> Rows { get; set; } = new();

        public List<ItemInput> Items { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(GroupId, cancellationToken);
            if (Group.Type != "Recurring")
            {
                TempData["ErrorMessage"] = "Chỉ nhóm loại \"Dùng lại nhiều lần\" mới có khoản chi định kỳ.";
                return RedirectToPage("/Groups/Details", new { id = GroupId });
            }

            Templates = await _apiClient.GetRecurringExpensesAsync(GroupId, cancellationToken);
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
        Templates = await _apiClient.GetRecurringExpensesAsync(GroupId, cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var payers = Input.Rows
            .Where(r => r.PayerAmount is > 0)
            .Select(r => new SplitBill.Application.Expenses.ExpensePayerInput(r.MemberId, r.PayerAmount!.Value))
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

        var firstRunAt = new DateTimeOffset(Input.FirstRunAt, TimeZoneInfo.Local.GetUtcOffset(Input.FirstRunAt));

        try
        {
            await _apiClient.CreateRecurringExpenseAsync(
                GroupId,
                new CreateRecurringExpenseRequest(
                    Input.Title, Input.TotalAmount, Input.ExtraFeeAmount, payers, Input.SplitMode, splitConfig!,
                    Input.Interval, firstRunAt, Input.Note, Input.Category),
                cancellationToken);

            TempData["SuccessMessage"] = "Đã tạo khoản chi định kỳ.";
            return RedirectToPage("/Groups/RecurringExpenses", new { groupId = GroupId });
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeactivateAsync(Guid templateId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.DeactivateRecurringExpenseAsync(templateId, cancellationToken);
            TempData["SuccessMessage"] = "Đã tắt khoản chi định kỳ.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Groups/RecurringExpenses", new { groupId = GroupId });
    }
}
