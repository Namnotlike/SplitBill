namespace SplitBill.Application.Settlements;

/// <summary>`/groups/{id}/balances` và `/groups/{id}/settlement-plan` (CLAUDE.md mục 8).</summary>
public interface IBalanceService
{
    Task<IReadOnlyList<MemberBalanceDto>> GetBalancesAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    Task<SettlementPlanDto> GetSettlementPlanAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Số dư của người dùng hiện tại trong TỪNG nhóm họ đang là thành viên (CLAUDE.md mục
    /// 15.4) — dùng cho bảng tổng quan cá nhân ở trang chủ. Không trả tổng gộp vì các nhóm có thể
    /// khác đơn vị tiền tệ (mục 14).</summary>
    Task<IReadOnlyList<PersonalGroupBalanceDto>> GetMyOverviewAsync(Guid callerUserId, CancellationToken cancellationToken);

    /// <summary>"Ai đang nợ tôi / tôi đang nợ ai" gộp theo TỪNG NGƯỜI, xuyên mọi nhóm đang tham gia
    /// (CLAUDE.md mục 20) — khác <see cref="GetMyOverviewAsync"/> vốn gộp theo từng nhóm.</summary>
    Task<IReadOnlyList<CounterpartyBalanceDto>> GetCounterpartyBalancesAsync(Guid callerUserId, CancellationToken cancellationToken);
}
