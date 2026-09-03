using SplitBill.Application.Common;

namespace SplitBill.Application.Splitting;

public sealed record ExpenseSplitResult(IReadOnlyList<MemberAmount> Splits, IReadOnlyList<Warning> Warnings);

/// <summary>Tính splits từ input gốc của người dùng theo CLAUDE.md mục 5.1–5.2.</summary>
public interface IExpenseSplitCalculator
{
    ExpenseSplitResult Calculate(ExpenseSplitInput input);
}
