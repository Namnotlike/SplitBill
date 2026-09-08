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

    private static readonly HashSet<string> AllowedReceiptExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxReceiptImageBytes = 10 * 1024 * 1024; // 10MB — khớp giới hạn ở ExpensesController (Api)

    [BindProperty(SupportsGet = true)]
    public Guid ExpenseId { get; set; }

    public GroupDto Group { get; set; } = null!;

    [BindProperty]
    public ExpenseInput Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    /// <summary>Khác null nếu khoản chi đã có ảnh hóa đơn — trang dùng để quyết định hiện thumbnail.</summary>
    public string? ReceiptImageUrl { get; set; }

    // ===== Bình luận khoản chi (CLAUDE.md mục 19) =====
    public IReadOnlyList<ExpenseCommentDto> Comments { get; set; } = Array.Empty<ExpenseCommentDto>();

    /// <summary>GroupMemberId của người đang xem trong nhóm này — dùng để quyết định hiện nút "Xóa"
    /// (chỉ tác giả bình luận hoặc Owner mới xóa được, khớp quyền phía API).</summary>
    public Guid? MyMemberId { get; set; }

    public bool IsOwner { get; set; }

    [BindProperty]
    public string NewCommentContent { get; set; } = string.Empty;

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

        // Nhãn/danh mục khoản chi (CLAUDE.md mục 15.3) — bổ sung 2026-09-05.
        public string Category { get; set; } = "Other";

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
            ReceiptImageUrl = expense.ReceiptImageUrl;
            await LoadCommentContextAsync(cancellationToken);
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
            await LoadCommentContextAsync(cancellationToken);
            return Page();
        }

        var payers = Input.Rows
            .Where(r => r.PayerAmount is > 0)
            .Select(r => new ExpensePayerInput(r.MemberId, r.PayerAmount!.Value))
            .ToList();

        if (payers.Count == 0)
        {
            ErrorMessage = "Cần ít nhất 1 người ứng tiền (Amount > 0).";
            await LoadCommentContextAsync(cancellationToken);
            return Page();
        }

        var splitConfig = ExpenseFormHelpers.BuildSplitConfig(Input.SplitMode, Input.Rows, Input.Items, out var configError);
        if (configError is not null)
        {
            ErrorMessage = configError;
            await LoadCommentContextAsync(cancellationToken);
            return Page();
        }

        var occurredAt = new DateTimeOffset(Input.OccurredAt, TimeZoneInfo.Local.GetUtcOffset(Input.OccurredAt));

        try
        {
            var result = await _apiClient.UpdateExpenseAsync(
                ExpenseId,
                new UpdateExpenseRequest(Input.Title, Input.TotalAmount, Input.ExtraFeeAmount, occurredAt, payers, Input.SplitMode, splitConfig!, Input.RowVersion, Input.Note, Category: Input.Category),
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
            await LoadCommentContextAsync(cancellationToken);
            return Page();
        }
    }

    // CLAUDE.md mục 19 — Bình luận khoản chi.
    public async Task<IActionResult> OnPostAddCommentAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(NewCommentContent))
        {
            try
            {
                await _apiClient.AddExpenseCommentAsync(ExpenseId, new CreateExpenseCommentRequest(NewCommentContent), cancellationToken);
            }
            catch (ApiException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }
        }

        return RedirectToPage(new { expenseId = ExpenseId });
    }

    public async Task<IActionResult> OnPostDeleteCommentAsync(Guid commentId, CancellationToken cancellationToken)
    {
        try
        {
            await _apiClient.DeleteExpenseCommentAsync(commentId, cancellationToken);
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage(new { expenseId = ExpenseId });
    }

    private async Task LoadCommentContextAsync(CancellationToken cancellationToken)
    {
        Comments = await _apiClient.GetExpenseCommentsAsync(ExpenseId, cancellationToken);

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var myMember = Group.Members.FirstOrDefault(m => m.UserId?.ToString() == userId);
        MyMemberId = myMember?.Id;
        IsOwner = myMember?.Role == "Owner";
    }

    public async Task<IActionResult> OnPostUploadReceiptAsync(IFormFile? receiptFile, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.GetExpenseAsync(ExpenseId, cancellationToken);

        if (receiptFile is null || receiptFile.Length == 0)
        {
            TempData["ErrorMessage"] = "Vui lòng chọn 1 file ảnh.";
            return RedirectToPage(new { expenseId = ExpenseId });
        }

        if (receiptFile.Length > MaxReceiptImageBytes)
        {
            TempData["ErrorMessage"] = "Ảnh hóa đơn tối đa 10MB.";
            return RedirectToPage(new { expenseId = ExpenseId });
        }

        if (!AllowedReceiptExtensions.Contains(Path.GetExtension(receiptFile.FileName)))
        {
            TempData["ErrorMessage"] = "Chỉ chấp nhận ảnh .jpg, .jpeg, .png, .webp.";
            return RedirectToPage(new { expenseId = ExpenseId });
        }

        try
        {
            await using var stream = receiptFile.OpenReadStream();
            await _apiClient.UploadReceiptImageAsync(ExpenseId, stream, receiptFile.FileName, receiptFile.ContentType, cancellationToken);
            TempData["SuccessMessage"] = "Đã tải ảnh hóa đơn.";
        }
        catch (ApiException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage(new { expenseId = existing.Id });
    }

    /// <summary>
    /// Proxy ảnh hóa đơn qua Web — trình duyệt không bao giờ gọi thẳng Api (thiếu Bearer token sẽ bị
    /// 401), luôn phải qua handler này để <see cref="SplitBillApiClient"/> tự gắn token (CLAUDE.md
    /// mục 10b, BearerTokenHandler).
    /// </summary>
    public async Task<IActionResult> OnGetReceiptImageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var image = await _apiClient.GetReceiptImageAsync(ExpenseId, cancellationToken);
            return File(image.Content, image.ContentType, image.FileName);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            // HttpRequestException (Api không phản hồi được) trước đây không bị bắt ở đây — cùng lớp
            // bug đã sửa ở Index.cshtml.cs (CLAUDE.md mục 23.4).
            return NotFound();
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
            Category = expense.Category,
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
