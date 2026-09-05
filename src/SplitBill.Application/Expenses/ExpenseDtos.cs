using SplitBill.Application.Common;

namespace SplitBill.Application.Expenses;

public sealed record ExpenseMemberAmountDto(Guid MemberId, long Amount);

public sealed record ExpenseDto(
    Guid Id,
    Guid GroupId,
    string Title,
    long TotalAmount,
    long ExtraFeeAmount,
    string SplitMode,
    string? Note,
    string? ReceiptImageUrl,
    DateTimeOffset OccurredAt,
    IReadOnlyList<ExpenseMemberAmountDto> Payers,
    IReadOnlyList<ExpenseMemberAmountDto> Splits,
    string RowVersion,
    // Input gốc người dùng đã nhập lúc tạo/sửa (JSON của SplitConfigInput — CLAUDE.md mục 4.1), để
    // client (form Edit) khôi phục đúng trọng số/%/danh sách món ăn thay vì suy ngược từ Amount cuối
    // cùng. Bổ sung 2026-09-05 — trước đó DB đã lưu field này nhưng API chưa từng trả ra.
    string? SplitConfigJson,
    // Nhãn/danh mục khoản chi (CLAUDE.md mục 15.3) — bổ sung 2026-09-05.
    string Category = "Other");

public sealed record ExpenseResult(ExpenseDto Data, IReadOnlyList<Warning> Warnings);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Bộ lọc cho GET /groups/{id}/expenses (CLAUDE.md mục 15.2 — Tìm kiếm/lọc khoản chi, bổ
/// sung 2026-09-05). Mọi field đều optional — field null nghĩa là không lọc theo tiêu chí đó. So khớp
/// <see cref="Title"/> không phân biệt hoa/thường, kiểu "chứa" (contains), không phải khớp tuyệt đối.</summary>
public sealed record ExpenseFilter(
    string? Title = null,
    Guid? PayerMemberId = null,
    DateTimeOffset? FromDate = null,
    DateTimeOffset? ToDate = null,
    long? MinAmount = null,
    long? MaxAmount = null,
    // Nhãn/danh mục (CLAUDE.md mục 15.3) — tên enum ExpenseCategory dạng string, null = không lọc.
    string? Category = null)
{
    public static readonly ExpenseFilter Empty = new();

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title) && PayerMemberId is null && FromDate is null && ToDate is null
        && MinAmount is null && MaxAmount is null && string.IsNullOrWhiteSpace(Category);
}

// ===== Input DTOs khớp body ở CLAUDE.md mục 8 =====

public sealed record ExpensePayerInput(Guid MemberId, long Amount);

public sealed record SharesInput(Guid MemberId, decimal Weight);

public sealed record PercentageInput(Guid MemberId, decimal Percent);

public sealed record ExactAmountInput(Guid MemberId, long Amount);

public sealed record ItemizedInput(string Name, long Price, IReadOnlyList<Guid> ConsumerMemberIds);

public sealed record SplitConfigInput(
    IReadOnlyList<Guid>? MemberIds = null,
    IReadOnlyList<SharesInput>? Shares = null,
    IReadOnlyList<PercentageInput>? Percentages = null,
    IReadOnlyList<ExactAmountInput>? ExactAmounts = null,
    IReadOnlyList<ItemizedInput>? Items = null);

public sealed record CreateExpenseRequest(
    string Title,
    long TotalAmount,
    long ExtraFeeAmount,
    DateTimeOffset OccurredAt,
    IReadOnlyList<ExpensePayerInput> Payers,
    string SplitMode,
    SplitConfigInput SplitConfig,
    string? Note = null,
    string? ReceiptImageUrl = null,
    // Nhãn/danh mục (CLAUDE.md mục 15.3) — null/rỗng mặc định "Other".
    string? Category = null);

public sealed record UpdateExpenseRequest(
    string Title,
    long TotalAmount,
    long ExtraFeeAmount,
    DateTimeOffset OccurredAt,
    IReadOnlyList<ExpensePayerInput> Payers,
    string SplitMode,
    SplitConfigInput SplitConfig,
    string RowVersion,
    string? Note = null,
    string? ReceiptImageUrl = null,
    string? Category = null);

public sealed record PreviewSplitRequest(
    long TotalAmount,
    long ExtraFeeAmount,
    string SplitMode,
    SplitConfigInput SplitConfig);

public sealed record PreviewSplitResult(IReadOnlyList<ExpenseMemberAmountDto> Splits, IReadOnlyList<Warning> Warnings);

public sealed record ReceiptImageContentDto(byte[] Content, string ContentType, string FileName);
