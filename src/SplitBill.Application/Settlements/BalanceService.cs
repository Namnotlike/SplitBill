using SplitBill.Application.Abstractions;
using SplitBill.Application.Settlement;
using SplitBill.Application.Splitting;
using SplitBill.Application.VietQr;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Enums;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Settlements;

/// <summary>Cài đặt CLAUDE.md mục 6 (balances + settlement-plan, cả 2 chế độ SimplifyDebts).</summary>
public sealed class BalanceService : IBalanceService
{
    private readonly IGroupRepository _groupRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IBalanceCalculator _balanceCalculator;
    private readonly IVietQrGenerator _vietQrGenerator;

    public BalanceService(
        IGroupRepository groupRepository,
        IExpenseRepository expenseRepository,
        ISettlementRepository settlementRepository,
        IBalanceCalculator balanceCalculator,
        IVietQrGenerator vietQrGenerator)
    {
        _groupRepository = groupRepository;
        _expenseRepository = expenseRepository;
        _settlementRepository = settlementRepository;
        _balanceCalculator = balanceCalculator;
        _vietQrGenerator = vietQrGenerator;
    }

    public async Task<IReadOnlyList<MemberBalanceDto>> GetBalancesAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        var balances = await ComputeBalancesAsync(group, cancellationToken);
        return balances.Select(b => new MemberBalanceDto(b.MemberId, NameOf(group, b.MemberId), b.Net)).ToList();
    }

    public async Task<SettlementPlanDto> GetSettlementPlanAsync(Guid callerUserId, Guid groupId, CancellationToken cancellationToken)
    {
        var group = await LoadGroupAsync(groupId, cancellationToken);
        ResolveCallerMember(group, callerUserId);

        if (group.SimplifyDebts)
        {
            return await BuildSimplifiedPlanAsync(group, cancellationToken);
        }

        return await BuildDirectPlanAsync(group, cancellationToken);
    }

    private async Task<SettlementPlanDto> BuildSimplifiedPlanAsync(Group group, CancellationToken cancellationToken)
    {
        var balances = await ComputeBalancesAsync(group, cancellationToken);

        var nonZeroCount = balances.Count(b => b.Net != 0);
        var solver = SettlementSolverSelector.Choose(nonZeroCount);
        var transactions = solver.Solve(balances);

        var dtos = transactions
            .Select(t => new SettlementTransactionDto(
                t.FromMemberId, NameOf(group, t.FromMemberId),
                t.ToMemberId, NameOf(group, t.ToMemberId),
                t.Amount,
                BuildVietQr(group, t.ToMemberId, t.Amount)))
            .ToList();

        return new SettlementPlanDto(Simplified: true, dtos.Count, dtos);
    }

    /// <summary>
    /// Chế độ SimplifyDebts = false (CLAUDE.md mục 6.5/6.6): sinh nợ trực tiếp theo từng expense,
    /// gộp các cặp (from, to) trùng nhau, rồi trừ phần đã Confirmed cho đúng cặp đó.
    /// Giản lược so với 6.6: phần "dồn phần dư sang net tổng" khi trả dư CHƯA triển khai — nếu
    /// paid[X,Y] > raw[X,Y] thì chỉ ẩn cặp đó (floor 0), không dồn phần dư sang chỗ khác.
    /// </summary>
    private async Task<SettlementPlanDto> BuildDirectPlanAsync(Group group, CancellationToken cancellationToken)
    {
        var expenses = await _expenseRepository.GetAllByGroupIdAsync(group.Id, cancellationToken);
        var settlements = await _settlementRepository.GetAllByGroupIdAsync(group.Id, cancellationToken);

        var raw = new Dictionary<(Guid From, Guid To), long>();

        foreach (var expense in expenses)
        {
            var payerWeights = expense.Payers
                .Where(p => p.Amount > 0)
                .ToDictionary(p => p.GroupMemberId, p => (decimal)p.Amount);

            if (payerWeights.Count == 0)
            {
                continue;
            }

            foreach (var split in expense.Splits)
            {
                if (split.Amount <= 0)
                {
                    continue;
                }

                var allocation = RoundingAllocator.AllocateLargestRemainder(split.Amount, payerWeights);
                foreach (var (payerId, amount) in allocation)
                {
                    if (amount <= 0 || payerId == split.GroupMemberId)
                    {
                        continue; // không tự nợ chính mình
                    }

                    var key = (From: split.GroupMemberId, To: payerId);
                    raw[key] = raw.GetValueOrDefault(key) + amount;
                }
            }
        }

        var paid = new Dictionary<(Guid From, Guid To), long>();
        foreach (var settlement in settlements.Where(s => s.Status == SettlementStatus.Confirmed))
        {
            var key = (From: settlement.FromMemberId, To: settlement.ToMemberId);
            paid[key] = paid.GetValueOrDefault(key) + settlement.Amount;
        }

        var transactions = new List<SettlementTransactionDto>();
        foreach (var (key, rawAmount) in raw)
        {
            var remaining = rawAmount - paid.GetValueOrDefault(key);
            if (remaining <= 0)
            {
                continue;
            }

            transactions.Add(new SettlementTransactionDto(
                key.From, NameOf(group, key.From),
                key.To, NameOf(group, key.To),
                remaining,
                BuildVietQr(group, key.To, remaining)));
        }

        return new SettlementPlanDto(Simplified: false, transactions.Count, transactions);
    }

    private async Task<IReadOnlyList<MemberBalance>> ComputeBalancesAsync(Group group, CancellationToken cancellationToken)
    {
        var expenses = await _expenseRepository.GetAllByGroupIdAsync(group.Id, cancellationToken);
        var settlements = await _settlementRepository.GetAllByGroupIdAsync(group.Id, cancellationToken);

        var expenseInputs = expenses.Select(e => new ExpenseBalanceInput(
            e.Id,
            e.Payers.Select(p => new MemberAmount(p.GroupMemberId, p.Amount)).ToList(),
            e.Splits.Select(s => new MemberAmount(s.GroupMemberId, s.Amount)).ToList()));

        var settlementInputs = settlements.Select(s => new SettlementBalanceInput(s.FromMemberId, s.ToMemberId, s.Amount, s.Status));

        return _balanceCalculator.Calculate(expenseInputs, settlementInputs);
    }

    private async Task<Group> LoadGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await _groupRepository.GetByIdWithMembersAsync(groupId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Không tìm thấy nhóm.");

    private static GroupMember ResolveCallerMember(Group group, Guid callerUserId) =>
        group.Members.FirstOrDefault(m => m.UserId == callerUserId && m.IsActive)
            ?? throw new DomainException(ErrorCodes.MemberNotInGroup, "Bạn không phải thành viên của nhóm này.");

    /// <summary>Sinh VietQR cho người NHẬN; trả null nếu chưa khai báo tài khoản (CLAUDE.md mục 9).</summary>
    private VietQrDto? BuildVietQr(Group group, Guid toMemberId, long amount)
    {
        var toMember = group.Members.FirstOrDefault(m => m.Id == toMemberId);
        var bankBin = toMember?.User?.BankBin;
        var accountNumber = toMember?.User?.BankAccountNumber;

        if (string.IsNullOrWhiteSpace(bankBin) || string.IsNullOrWhiteSpace(accountNumber))
        {
            return null;
        }

        var content = _vietQrGenerator.BuildTransferContent(group.Name);
        var payload = _vietQrGenerator.Generate(bankBin, accountNumber, amount, content);
        return new VietQrDto(bankBin, accountNumber, amount, content, payload);
    }

    private static string NameOf(Group group, Guid memberId) =>
        group.Members.FirstOrDefault(m => m.Id == memberId)?.DisplayName ?? "(đã rời nhóm)";
}
