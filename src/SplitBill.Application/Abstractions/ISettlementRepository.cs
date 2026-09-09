using SettlementEntity = SplitBill.Domain.Entities.Settlement;

namespace SplitBill.Application.Abstractions;

public interface ISettlementRepository
{
    /// <summary>Toàn bộ settlement chưa xóa của nhóm — dùng cho BalanceCalculator.</summary>
    Task<List<SettlementEntity>> GetAllByGroupIdAsync(Guid groupId, CancellationToken cancellationToken);

    Task<SettlementEntity?> GetByIdAsync(Guid settlementId, CancellationToken cancellationToken);

    /// <summary>Nạp Settlement BẤT KỂ đã xóa hay chưa (dùng <c>IgnoreQueryFilters</c>) — dùng cho
    /// tính năng khôi phục (CLAUDE.md mục 24).</summary>
    Task<SettlementEntity?> GetByIdIncludingDeletedAsync(Guid settlementId, CancellationToken cancellationToken);

    /// <summary>Toàn bộ settlement ĐÃ xóa của nhóm (IsDeleted = true) — dùng cho màn hình "Đã xóa gần
    /// đây" (CLAUDE.md mục 24).</summary>
    Task<List<SettlementEntity>> GetDeletedByGroupIdAsync(Guid groupId, CancellationToken cancellationToken);

    Task AddAsync(SettlementEntity settlement, CancellationToken cancellationToken);

    /// <summary>Mọi settlement (mọi nhóm) còn Pending và chưa được nhắc gần đây — dùng bởi
    /// <c>DebtReminderRunner</c> (CLAUDE.md mục 15.8). "Chưa được nhắc gần đây" = lần nhắc gần nhất
    /// (hoặc lúc tạo nếu chưa từng nhắc) đã cách <paramref name="cutoff"/> trở lên.</summary>
    Task<List<SettlementEntity>> GetPendingDueForReminderAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
