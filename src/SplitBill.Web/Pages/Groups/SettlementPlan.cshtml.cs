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

    // Miễn nợ (CLAUDE.md mục 25.2) — form riêng, cùng shape với RecordInput nhưng tách property để 2
    // form trên trang không giẫm lên nhau khi model-binding (mỗi form chỉ gửi đúng field của chính nó).
    [BindProperty]
    public RecordInput WaiveDebt { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public sealed class RecordInput
    {
        [Required]
        public Guid FromMemberId { get; set; }

        [Required]
        public Guid ToMemberId { get; set; }

        // ⚠️ CỐ TÌNH không gắn [Range(1, long.MaxValue)] ở đây: Razor Pages validate MỌI property
        // [BindProperty] trên PageModel mỗi lần POST, bất kể handler nào chạy — nếu Amount có [Range]
        // ở CẢ NewSettlement lẫn WaiveDebt, submit form "Ghi nhận" (chỉ điền NewSettlement.*) sẽ khiến
        // WaiveDebt.Amount giữ giá trị mặc định 0, tự động fail Range và làm ModelState.IsValid = false
        // cho TOÀN BỘ request — chặn nhầm cả luồng ghi nhận thanh toán bình thường dù người dùng không
        // hề đụng tới form Miễn nợ. Phát hiện lúc thêm WaiveDebt vào cùng PageModel, trước khi kịp
        // verify sống (không phải bug đã từng xảy ra thật, nhưng đủ rõ ràng để sửa ngay từ đầu thay vì
        // dựa vào ModelState.IsValid). Validate Amount > 0 thủ công trong từng handler thay thế.
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
        if (NewSettlement.Amount <= 0 || NewSettlement.FromMemberId == NewSettlement.ToMemberId)
        {
            TempData["ErrorMessage"] = "Người chuyển và người nhận không được trùng nhau, số tiền phải > 0.";
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

    // CLAUDE.md mục 25.2 — miễn nợ. Không dùng chung handler "Record" ở trên dù shape input giống hệt
    // nhau: 2 endpoint API khác nhau (waive tạo thẳng Confirmed, record tạo Pending) và thông báo
    // thành công cũng khác — tách handler riêng cho rõ ràng, tránh 1 tham số ẩn kiểu "isWaive: bool"
    // rẽ nhánh trong cùng 1 handler.
    public async Task<IActionResult> OnPostWaiveAsync(Guid id, CancellationToken cancellationToken)
    {
        if (WaiveDebt.Amount <= 0 || WaiveDebt.FromMemberId == WaiveDebt.ToMemberId)
        {
            TempData["ErrorMessage"] = "Người nợ và người miễn nợ không được trùng nhau, số tiền phải > 0.";
            return RedirectToPage("/Groups/SettlementPlan", new { id });
        }

        try
        {
            await _apiClient.WaiveSettlementAsync(
                id,
                new WaiveSettlementRequest(WaiveDebt.FromMemberId, WaiveDebt.ToMemberId, WaiveDebt.Amount, WaiveDebt.Note),
                cancellationToken);
            TempData["SuccessMessage"] = "Đã miễn nợ.";
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
