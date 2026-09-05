using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlements;

namespace SplitBill.Application.Export;

public sealed class ExportService : IExportService
{
    private readonly IExpenseService _expenseService;
    private readonly IGroupService _groupService;
    private readonly IBalanceService _balanceService;

    public ExportService(IExpenseService expenseService, IGroupService groupService, IBalanceService balanceService)
    {
        _expenseService = expenseService;
        _groupService = groupService;
        _balanceService = balanceService;
    }

    public async Task<string> ExportExpensesCsvAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await _groupService.GetByIdAsync(callerUserId, groupId, cancellationToken);
        var memberNames = group.Members.ToDictionary(m => m.Id, m => m.DisplayName);
        var expenses = await _expenseService.GetAllForExportAsync(callerUserId, groupId, cancellationToken);

        var csv = new CsvBuilder();
        csv.AddRow("Ngày", "Tiêu đề", "Danh mục", "Tổng tiền", "Phụ phí", "Cách chia", "Ai ứng", "Ai chịu", "Ghi chú");

        foreach (var expense in expenses)
        {
            csv.AddRow(
                expense.OccurredAt.ToString("yyyy-MM-dd HH:mm"),
                expense.Title,
                expense.Category,
                expense.TotalAmount,
                expense.ExtraFeeAmount,
                expense.SplitMode,
                FormatMemberAmounts(expense.Payers, memberNames),
                FormatMemberAmounts(expense.Splits, memberNames),
                expense.Note ?? string.Empty);
        }

        return csv.ToString();
    }

    public async Task<string> ExportBalancesCsvAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var balances = await _balanceService.GetBalancesAsync(callerUserId, groupId, cancellationToken);

        var csv = new CsvBuilder();
        csv.AddRow("Thành viên", "Số dư (dương = được nhận, âm = còn nợ)");

        foreach (var balance in balances.OrderByDescending(b => b.Net))
        {
            csv.AddRow(balance.MemberName, balance.Net);
        }

        return csv.ToString();
    }

    private static string FormatMemberAmounts(IReadOnlyList<ExpenseMemberAmountDto> amounts, IReadOnlyDictionary<Guid, string> memberNames) =>
        string.Join("; ", amounts.Select(a =>
            $"{memberNames.GetValueOrDefault(a.MemberId, "(đã rời nhóm)")}: {a.Amount}"));
}
