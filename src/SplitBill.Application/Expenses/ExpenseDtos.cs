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
    string? SplitConfigJson);

public sealed record ExpenseResult(ExpenseDto Data, IReadOnlyList<Warning> Warnings);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

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
    string? ReceiptImageUrl = null);

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
    string? ReceiptImageUrl = null);

public sealed record PreviewSplitRequest(
    long TotalAmount,
    long ExtraFeeAmount,
    string SplitMode,
    SplitConfigInput SplitConfig);

public sealed record PreviewSplitResult(IReadOnlyList<ExpenseMemberAmountDto> Splits, IReadOnlyList<Warning> Warnings);

public sealed record ReceiptImageContentDto(byte[] Content, string ContentType, string FileName);
