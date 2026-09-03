using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IReceiptImageRepository
{
    Task<ReceiptImage?> GetByExpenseIdAsync(Guid expenseId, CancellationToken cancellationToken);

    /// <summary>Thay thế ảnh cũ của expense (nếu có) bằng ảnh mới — mỗi expense chỉ giữ 1 ảnh.</summary>
    Task UpsertAsync(ReceiptImage image, CancellationToken cancellationToken);
}
