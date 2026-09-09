namespace SplitBill.Application.Expenses;

public interface IExpenseService
{
    Task<PagedResult<ExpenseDto>> GetPagedAsync(Guid callerUserId, Guid groupId, int page, int pageSize, ExpenseFilter filter, CancellationToken cancellationToken);

    /// <summary>Toàn bộ expense chưa xóa của nhóm (không phân trang) — dùng cho xuất dữ liệu.</summary>
    Task<IReadOnlyList<ExpenseDto>> GetAllForExportAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);
    Task<ExpenseDto> GetByIdAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken);
    Task<ExpenseResult> CreateAsync(Guid callerUserId, Guid groupId, CreateExpenseRequest request, CancellationToken cancellationToken);
    Task<ExpenseResult> UpdateAsync(Guid callerUserId, Guid expenseId, UpdateExpenseRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken);

    /// <summary>Danh sách khoản chi ĐÃ xóa (soft-delete) của nhóm — CLAUDE.md mục 24.</summary>
    Task<IReadOnlyList<ExpenseDto>> GetDeletedAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Khôi phục 1 khoản chi đã xóa — CLAUDE.md mục 24. Ném <c>EXPENSE_NOT_DELETED</c> nếu
    /// khoản chi hiện KHÔNG ở trạng thái đã xóa.</summary>
    Task<ExpenseDto> RestoreAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken);

    /// <summary>Upload ảnh hóa đơn, trả về ExpenseDto với ReceiptImageUrl đã cập nhật (CLAUDE.md mục 8, M5).</summary>
    Task<ExpenseDto> UploadReceiptImageAsync(Guid callerUserId, Guid expenseId, Stream content, string fileName, string contentType, CancellationToken cancellationToken);

    /// <summary>Tải ảnh hóa đơn — yêu cầu là thành viên nhóm (CLAUDE.md mục 8).</summary>
    Task<ReceiptImageContentDto> GetReceiptImageAsync(Guid callerUserId, Guid expenseId, CancellationToken cancellationToken);

    /// <summary>Tính thử splits mà không lưu — pure, không cần truy vấn DB (CLAUDE.md mục 8).</summary>
    PreviewSplitResult PreviewSplit(PreviewSplitRequest request);
}
