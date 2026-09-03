using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IExpenseRepository
{
    /// <summary>Nạp Expense kèm Payers/Splits (chỉ expense chưa xóa).</summary>
    Task<Expense?> GetByIdAsync(Guid expenseId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<Expense> Items, int TotalCount)> GetPagedAsync(
        Guid groupId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Toàn bộ expense chưa xóa của nhóm, kèm Payers/Splits — dùng cho BalanceCalculator.</summary>
    Task<List<Expense>> GetAllByGroupIdAsync(Guid groupId, CancellationToken cancellationToken);

    Task AddAsync(Expense expense, CancellationToken cancellationToken);

    /// <summary>
    /// Xóa tường minh các ExpensePayer/ExpenseSplit cũ khi Update thay toàn bộ danh sách.
    /// BẮT BUỘC dùng thay vì chỉ Clear() collection navigation — vì FK Expense→Payers/Splits cấu
    /// hình DeleteBehavior.Restrict (CLAUDE.md mục 4.3), EF Core sẽ ném
    /// "relationship severed" exception nếu chỉ Clear() mà không Remove() tường minh khỏi DbSet.
    /// </summary>
    void RemovePayers(IEnumerable<ExpensePayer> payers);

    void RemoveSplits(IEnumerable<ExpenseSplit> splits);

    /// <summary>Thêm tường minh qua DbSet (không dựa vào graph-tracking từ navigation) để tránh
    /// EF Core nhầm entity mới là Update thay vì Insert khi gán lại cả collection.</summary>
    void AddPayers(IEnumerable<ExpensePayer> payers);

    void AddSplits(IEnumerable<ExpenseSplit> splits);
}
