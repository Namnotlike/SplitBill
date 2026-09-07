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

    // ===== Preset cách chia hay dùng (CLAUDE.md mục 21) =====
    public IReadOnlyList<SplitPresetDto> Presets { get; set; } = Array.Empty<SplitPresetDto>();

    [BindProperty]
    public string? PresetName { get; set; }

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

        // Nhãn/danh mục khoản chi (CLAUDE.md mục 15.3) — bổ sung 2026-09-05.
        public string Category { get; set; } = "Other";

        public List<MemberRowInput> Rows { get; set; } = new();

        public List<ItemInput> Items { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(GroupId, cancellationToken);
            Input.Rows = Group.Members.Select(m => new MemberRowInput { MemberId = m.Id, EqualParticipant = true }).ToList();
            Presets = await _apiClient.GetSplitPresetsAsync(GroupId, cancellationToken);
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
            await LoadPresetsAsync(cancellationToken);
            return Page();
        }

        var payers = Input.Rows
            .Where(r => r.PayerAmount is > 0)
            .Select(r => new ExpensePayerInput(r.MemberId, r.PayerAmount!.Value))
            .ToList();

        if (payers.Count == 0)
        {
            ErrorMessage = "Cần ít nhất 1 người ứng tiền (Amount > 0).";
            await LoadPresetsAsync(cancellationToken);
            return Page();
        }

        var splitConfig = ExpenseFormHelpers.BuildSplitConfig(Input.SplitMode, Input.Rows, Input.Items, out var configError);
        if (configError is not null)
        {
            ErrorMessage = configError;
            await LoadPresetsAsync(cancellationToken);
            return Page();
        }

        var occurredAt = new DateTimeOffset(Input.OccurredAt, TimeZoneInfo.Local.GetUtcOffset(Input.OccurredAt));

        try
        {
            var result = await _apiClient.CreateExpenseAsync(
                GroupId,
                new CreateExpenseRequest(Input.Title, Input.TotalAmount, Input.ExtraFeeAmount, occurredAt, payers, Input.SplitMode, splitConfig!, Input.Note, Category: Input.Category),
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
            await LoadPresetsAsync(cancellationToken);
            return Page();
        }
    }

    // CLAUDE.md mục 21 — Preset cách chia hay dùng. Tái dùng ĐÚNG Input.SplitMode/Rows/Items đã bind
    // từ form chính (nút "Lưu thành preset" dùng formaction trỏ về đây, KHÔNG phải 1 <form> lồng
    // riêng — HTML không cho phép form lồng form) + ExpenseFormHelpers.BuildSplitConfig đã có sẵn,
    // không cần viết lại logic dựng SplitConfig ở phía client.
    public async Task<IActionResult> OnPostSavePresetAsync(CancellationToken cancellationToken)
    {
        Group = await _apiClient.GetGroupAsync(GroupId, cancellationToken);

        if (string.IsNullOrWhiteSpace(PresetName))
        {
            ErrorMessage = "Vui lòng nhập tên cho preset.";
            await LoadPresetsAsync(cancellationToken);
            return Page();
        }

        var splitConfig = ExpenseFormHelpers.BuildSplitConfig(Input.SplitMode, Input.Rows, Input.Items, out var configError);
        if (configError is not null)
        {
            ErrorMessage = configError;
            await LoadPresetsAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _apiClient.CreateSplitPresetAsync(GroupId, new CreateSplitPresetRequest(PresetName, Input.SplitMode, splitConfig!), cancellationToken);
            TempData["SuccessMessage"] = $"Đã lưu preset \"{PresetName}\".";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        // Quay lại trang trắng (không giữ lại dữ liệu đã nhập) — mục đích chính của thao tác này là
        // LƯU cách chia để dùng cho các khoản chi SAU này, không phải tiếp tục điền khoản chi hiện tại.
        return RedirectToPage(new { groupId = GroupId });
    }

    public async Task<IActionResult> OnPostDeletePresetAsync(Guid presetId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.DeleteSplitPresetAsync(presetId, cancellationToken);
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage(new { groupId = GroupId });
    }

    private async Task LoadPresetsAsync(CancellationToken cancellationToken)
    {
        try
        {
            Presets = await _apiClient.GetSplitPresetsAsync(GroupId, cancellationToken);
        }
        catch (ApiException)
        {
        }
    }
}
