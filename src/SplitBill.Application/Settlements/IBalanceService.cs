namespace SplitBill.Application.Settlements;

/// <summary>`/groups/{id}/balances` và `/groups/{id}/settlement-plan` (CLAUDE.md mục 8).</summary>
public interface IBalanceService
{
    Task<IReadOnlyList<MemberBalanceDto>> GetBalancesAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    Task<SettlementPlanDto> GetSettlementPlanAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);
}
