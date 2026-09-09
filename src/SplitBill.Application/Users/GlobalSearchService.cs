using SplitBill.Application.Abstractions;
using SplitBill.Application.Expenses;

namespace SplitBill.Application.Users;

/// <summary>Cài đặt CLAUDE.md mục 25.8. Không viết lại logic đọc dữ liệu nào mới — thuần orchestrate
/// <see cref="IGroupRepository.GetByUserIdAsync"/> (đã lọc đúng nhóm caller đang active, cùng mẫu
/// <see cref="UserDashboardService"/> mục 25.5/<see cref="Settlements.BalanceService.
/// GetCounterpartyBalancesAsync"/> mục 20) + <see cref="IExpenseService.GetPagedAsync"/> với
/// <see cref="ExpenseFilter.Title"/> (đã có sẵn từ mục 15.2, đẩy việc lọc "chứa, không phân biệt hoa
/// thường" xuống tận DB thay vì tự tải hết rồi lọc ở C#).</summary>
public sealed class GlobalSearchService : IGlobalSearchService
{
    // Giới hạn kết quả để 1 lượt tìm kiếm không bao giờ trả về hàng trăm dòng dù user có rất nhiều
    // nhóm/khoản chi — cùng tinh thần RecentActivityLimit/RecentActivityPerGroupPageSize ở
    // UserDashboardService (mục 25.5).
    private const int ExpensesPerGroupLimit = 10;
    private const int TotalExpenseResultsLimit = 30;

    private readonly IGroupRepository _groupRepository;
    private readonly IExpenseService _expenseService;

    public GlobalSearchService(IGroupRepository groupRepository, IExpenseService expenseService)
    {
        _groupRepository = groupRepository;
        _expenseService = expenseService;
    }

    public async Task<GlobalSearchResultDto> SearchAsync(Guid callerUserId, string? query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            // Không tìm kiếm gì với query rỗng — tránh 1 lượt quét toàn bộ nhóm/khoản chi vô nghĩa.
            return new GlobalSearchResultDto([], []);
        }

        var groups = await _groupRepository.GetByUserIdAsync(callerUserId, cancellationToken);

        var matchingGroups = new List<GroupSearchHitDto>();
        var matchingExpenses = new List<ExpenseSearchHitDto>();

        foreach (var group in groups)
        {
            var callerMember = group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive);
            if (callerMember is null)
            {
                continue; // GetByUserIdAsync đã lọc nhưng vẫn phòng thủ, cùng mẫu UserDashboardService.
            }

            if (group.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matchingGroups.Add(new GroupSearchHitDto(group.Id, group.Name, group.Type.ToString(), group.Currency));
            }

            var filter = new ExpenseFilter(Title: query);
            var expensePage = await _expenseService.GetPagedAsync(callerUserId, group.Id, 1, ExpensesPerGroupLimit, filter, cancellationToken);
            matchingExpenses.AddRange(expensePage.Items.Select(e =>
                new ExpenseSearchHitDto(e.Id, group.Id, group.Name, e.Title, e.TotalAmount, group.Currency, e.OccurredAt)));
        }

        return new GlobalSearchResultDto(
            matchingGroups.OrderBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase).ToList(),
            matchingExpenses.OrderByDescending(e => e.OccurredAt).Take(TotalExpenseResultsLimit).ToList());
    }
}
