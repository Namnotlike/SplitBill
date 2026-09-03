using SettlementEntity = SplitBill.Domain.Entities.Settlement;

namespace SplitBill.Application.Abstractions;

public interface ISettlementRepository
{
    /// <summary>Toàn bộ settlement chưa xóa của nhóm — dùng cho BalanceCalculator.</summary>
    Task<List<SettlementEntity>> GetAllByGroupIdAsync(Guid groupId, CancellationToken cancellationToken);

    Task<SettlementEntity?> GetByIdAsync(Guid settlementId, CancellationToken cancellationToken);

    Task AddAsync(SettlementEntity settlement, CancellationToken cancellationToken);
}
