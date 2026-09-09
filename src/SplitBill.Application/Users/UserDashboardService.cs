using SplitBill.Application.Abstractions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;

namespace SplitBill.Application.Users;

/// <summary>Cài đặt CLAUDE.md mục 25.5 — dashboard cá nhân nâng cao. Không đụng tới thuật toán settlement
/// (mục 6) hay lưu bất kỳ dữ liệu tổng hợp nào vào DB (mục 1 "không bao giờ lưu balance") — thuần
/// orchestrate lại các service đã có sẵn (Group/Expense/Settlement), cùng mẫu
/// <see cref="Settlements.BalanceService.GetCounterpartyBalancesAsync"/> (mục 20): loop qua
/// <c>IGroupRepository.GetByUserIdAsync</c> (đã lọc đúng nhóm caller đang active), gộp kết quả lại.</summary>
public sealed class UserDashboardService : IUserDashboardService
{
    private const int RecentActivityLimit = 10;
    private const int RecentActivityPerGroupPageSize = 5;

    private readonly IGroupRepository _groupRepository;
    private readonly IGroupService _groupService;
    private readonly IExpenseService _expenseService;
    private readonly ISettlementRecordService _settlementRecordService;

    public UserDashboardService(
        IGroupRepository groupRepository,
        IGroupService groupService,
        IExpenseService expenseService,
        ISettlementRecordService settlementRecordService)
    {
        _groupRepository = groupRepository;
        _groupService = groupService;
        _expenseService = expenseService;
        _settlementRecordService = settlementRecordService;
    }

    public async Task<PersonalDashboardDto> GetDashboardAsync(Guid callerUserId, CancellationToken cancellationToken)
    {
        var groups = await _groupRepository.GetByUserIdAsync(callerUserId, cancellationToken);

        var activeGroupCount = 0;
        var expensesThisMonth = 0;
        var pendingSettlements = new List<PendingSettlementToConfirmDto>();
        var recentActivity = new List<DashboardActivityDto>();
        var now = DateTimeOffset.UtcNow;

        foreach (var group in groups)
        {
            var callerMember = group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive);
            if (callerMember is null)
            {
                continue; // GetByUserIdAsync đã lọc nhưng vẫn phòng thủ, cùng mẫu BalanceService.
            }

            activeGroupCount++;

            var expenses = await _expenseService.GetAllForExportAsync(callerUserId, group.Id, cancellationToken);
            expensesThisMonth += expenses.Count(e => e.OccurredAt.Year == now.Year && e.OccurredAt.Month == now.Month);

            var settlements = await _settlementRecordService.GetByGroupAsync(callerUserId, group.Id, cancellationToken);
            var memberNames = group.Members.ToDictionary(m => m.Id, m => m.DisplayName);
            pendingSettlements.AddRange(settlements
                .Where(s => s.Status == "Pending" && s.ToMemberId == callerMember.Id)
                .Select(s => new PendingSettlementToConfirmDto(
                    s.Id, group.Id, group.Name, group.Currency,
                    memberNames.GetValueOrDefault(s.FromMemberId, "(đã rời nhóm)"),
                    s.Amount, s.CreatedAt)));

            // pageSize nhỏ (5) là đủ vì sau khi gộp CHỈ giữ lại tối đa RecentActivityLimit dòng mới
            // nhất trên toàn bộ nhóm — không cần tải nhiều hơn từ 1 nhóm duy nhất.
            var groupActivity = await _groupService.GetAuditLogsAsync(callerUserId, group.Id, 1, RecentActivityPerGroupPageSize, cancellationToken);
            recentActivity.AddRange(groupActivity.Items.Select(a => new DashboardActivityDto(group.Id, group.Name, a.Summary, a.CreatedAt)));
        }

        var topActivity = recentActivity.OrderByDescending(a => a.CreatedAt).Take(RecentActivityLimit).ToList();
        var sortedPending = pendingSettlements.OrderByDescending(s => s.CreatedAt).ToList();

        return new PersonalDashboardDto(activeGroupCount, expensesThisMonth, sortedPending, topActivity);
    }
}
