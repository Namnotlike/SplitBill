using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages.Groups;

public class SettlementPlanModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public SettlementPlanModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GroupDto Group { get; set; } = null!;
    public SettlementPlanDto Plan { get; set; } = null!;
    public IReadOnlyList<SettlementDto> Settlements { get; set; } = Array.Empty<SettlementDto>();

    /// <summary>GroupMemberId của user hiện tại trong nhóm này, null nếu không xác định được.</summary>
    public Guid? MyMemberId { get; set; }

    [BindProperty]
    public RecordInput NewSettlement { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public sealed class RecordInput
    {
        [Required]
        public Guid FromMemberId { get; set; }

        [Required]
        public Guid ToMemberId { get; set; }

        [Range(1, long.MaxValue)]
        public long Amount { get; set; }

        public string? Note { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            Group = await _apiClient.GetGroupAsync(id, cancellationToken);
            Plan = await _apiClient.GetSettlementPlanAsync(id, cancellationToken);
            Settlements = await _apiClient.GetSettlementsAsync(id, cancellationToken);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            MyMemberId = Group.Members.FirstOrDefault(m => m.UserId?.ToString() == userId)?.Id;

            return Page();
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage("/Groups/Index");
        }
    }

    public async Task<IActionResult> OnPostRecordAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || NewSettlement.FromMemberId == NewSettlement.ToMemberId)
        {
            TempData["ErrorMessage"] = "Người chuyển và người nhận không được trùng nhau.";
            return RedirectToPage("/Groups/SettlementPlan", new { id });
        }

        try
        {
            await _apiClient.CreateSettlementAsync(
                id,
                new CreateSettlementRequest(NewSettlement.FromMemberId, NewSettlement.ToMemberId, NewSettlement.Amount, NewSettlement.Note),
                cancellationToken);
            TempData["SuccessMessage"] = "Đã ghi nhận, chờ người nhận xác nhận.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Groups/SettlementPlan", new { id });
    }

    public async Task<IActionResult> OnPostConfirmAsync(Guid id, Guid settlementId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.ConfirmSettlementAsync(settlementId, cancellationToken);
            TempData["SuccessMessage"] = "Đã xác nhận thanh toán.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Groups/SettlementPlan", new { id });
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, Guid settlementId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.RejectSettlementAsync(settlementId, cancellationToken);
            TempData["SuccessMessage"] = "Đã từ chối.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Groups/SettlementPlan", new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, Guid settlementId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.DeleteSettlementAsync(settlementId, cancellationToken);
            TempData["SuccessMessage"] = "Đã xóa.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage("/Groups/SettlementPlan", new { id });
    }

    public string MemberName(Guid memberId) => Group.Members.FirstOrDefault(m => m.Id == memberId)?.DisplayName ?? "?";
}
