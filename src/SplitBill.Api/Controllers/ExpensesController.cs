using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Api.Auth;
using SplitBill.Application.Common;
using SplitBill.Application.Expenses;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Api.Controllers;

[ApiController]
[Authorize]
public sealed class ExpensesController : ControllerBase
{
    private const int DefaultPageSize = 20;

    private readonly IExpenseService _expenseService;
    private readonly IValidator<CreateExpenseRequest> _createValidator;
    private readonly IValidator<UpdateExpenseRequest> _updateValidator;
    private readonly IValidator<PreviewSplitRequest> _previewValidator;

    public ExpensesController(
        IExpenseService expenseService,
        IValidator<CreateExpenseRequest> createValidator,
        IValidator<UpdateExpenseRequest> updateValidator,
        IValidator<PreviewSplitRequest> previewValidator)
    {
        _expenseService = expenseService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _previewValidator = previewValidator;
    }

    [HttpGet("/api/v1/groups/{groupId:guid}/expenses")]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetPagedAsync(
        Guid groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? title = null,
        [FromQuery] Guid? payerMemberId = null,
        [FromQuery] DateTimeOffset? fromDate = null,
        [FromQuery] DateTimeOffset? toDate = null,
        [FromQuery] long? minAmount = null,
        [FromQuery] long? maxAmount = null,
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, 100);

        var filter = new ExpenseFilter(title, payerMemberId, fromDate, toDate, minAmount, maxAmount, category);
        var result = await _expenseService.GetPagedAsync(User.GetUserId(), groupId, page, pageSize, filter, cancellationToken);
        return Ok(result);
    }

    [HttpPost("/api/v1/groups/{groupId:guid}/expenses")]
    public async Task<ActionResult<ExpenseResult>> CreateAsync(Guid groupId, CreateExpenseRequest request, CancellationToken cancellationToken)
    {
        _createValidator.ValidateOrThrowDomainException(request);
        var result = await _expenseService.CreateAsync(User.GetUserId(), groupId, request, cancellationToken);
        // ASP.NET Core strip hậu tố "Async" khỏi ActionName mặc định — route đăng ký tên "GetById".
        return CreatedAtAction("GetById", new { expenseId = result.Data.Id }, result);
    }

    [HttpGet("/api/v1/expenses/{expenseId:guid}")]
    public async Task<ActionResult<ExpenseDto>> GetByIdAsync(Guid expenseId, CancellationToken cancellationToken)
    {
        var expense = await _expenseService.GetByIdAsync(User.GetUserId(), expenseId, cancellationToken);
        return Ok(expense);
    }

    [HttpPut("/api/v1/expenses/{expenseId:guid}")]
    public async Task<ActionResult<ExpenseResult>> UpdateAsync(Guid expenseId, UpdateExpenseRequest request, CancellationToken cancellationToken)
    {
        _updateValidator.ValidateOrThrowDomainException(request);
        var result = await _expenseService.UpdateAsync(User.GetUserId(), expenseId, request, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("/api/v1/expenses/{expenseId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid expenseId, CancellationToken cancellationToken)
    {
        await _expenseService.DeleteAsync(User.GetUserId(), expenseId, cancellationToken);
        return NoContent();
    }

    private static readonly HashSet<string> AllowedReceiptExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    // ⚠️ Bổ sung khi rà soát 2026-09-04: trước đây chỉ kiểm tra ĐUÔI file, không kiểm tra Content-Type
    // client gửi lên. Vì ContentType này được lưu thẳng và trả lại nguyên văn ở GET bên dưới, một
    // thành viên nhóm có thể up file đuôi ".jpg" nhưng Content-Type "text/html" chứa mã độc — rủi ro
    // stored-XSS phạm vi trong nhóm nếu sau này có UI hiển thị ảnh inline. Phải chặn ngay từ whitelist
    // Content-Type thật, không chỉ dựa vào đuôi file (đuôi file người dùng đặt tùy ý).
    private static readonly HashSet<string> AllowedReceiptContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp",
    };
    private const long MaxReceiptImageBytes = 10 * 1024 * 1024; // 10MB

    [HttpPost("/api/v1/expenses/{expenseId:guid}/receipt-image")]
    [RequestSizeLimit(MaxReceiptImageBytes)]
    public async Task<ActionResult<ExpenseDto>> UploadReceiptImageAsync(Guid expenseId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Thiếu file ảnh hóa đơn.");
        }

        if (file.Length > MaxReceiptImageBytes)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Ảnh hóa đơn tối đa 10MB.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedReceiptExtensions.Contains(extension))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Chỉ chấp nhận ảnh .jpg, .jpeg, .png, .webp.");
        }

        if (!AllowedReceiptContentTypes.Contains(file.ContentType))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Content-Type ảnh không hợp lệ. Chỉ chấp nhận image/jpeg, image/png, image/webp.");
        }

        await using var stream = file.OpenReadStream();
        var expense = await _expenseService.UploadReceiptImageAsync(
            User.GetUserId(), expenseId, stream, file.FileName, file.ContentType, cancellationToken);
        return Ok(expense);
    }

    [HttpGet("/api/v1/expenses/{expenseId:guid}/receipt-image")]
    public async Task<IActionResult> GetReceiptImageAsync(Guid expenseId, CancellationToken cancellationToken)
    {
        var image = await _expenseService.GetReceiptImageAsync(User.GetUserId(), expenseId, cancellationToken);
        // Phòng vệ thêm (defense in depth): dù ContentType đã được whitelist lúc upload, vẫn ép trình
        // duyệt không tự "đoán" (MIME-sniff) loại nội dung khác với Content-Type khai báo.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Content, image.ContentType, image.FileName);
    }

    [HttpPost("/api/v1/expenses/preview-split")]
    public ActionResult<PreviewSplitResult> PreviewSplit(PreviewSplitRequest request)
    {
        _previewValidator.ValidateOrThrowDomainException(request);
        var result = _expenseService.PreviewSplit(request);
        return Ok(result);
    }
}
