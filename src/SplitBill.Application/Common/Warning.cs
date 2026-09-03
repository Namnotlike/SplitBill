namespace SplitBill.Application.Common;

/// <summary>Hằng số warning code trả về trong response API (CLAUDE.md mục 5.1, 5.3, 8).</summary>
public static class WarningCodes
{
    /// <summary>Σ payers ≠ Σ splits của một khoản chi (CLAUDE.md mục 5.3).</summary>
    public const string SplitTotalMismatch = "SPLIT_TOTAL_MISMATCH";

    /// <summary>SplitMode.Percentage nhưng Σ phần trăm ≠ 100 (CLAUDE.md mục 5.1).</summary>
    public const string PercentageSumNotHundred = "PERCENTAGE_SUM_NOT_100";

    /// <summary>SplitMode.ExactAmount nhưng Σ số tiền nhập tay ≠ TotalAmount (CLAUDE.md mục 5.1).</summary>
    public const string ExactAmountSumMismatch = "EXACT_AMOUNT_SUM_MISMATCH";
}

/// <summary>Một cảnh báo không chặn lưu, trả kèm trong response (CLAUDE.md mục 8).</summary>
public sealed record Warning(string Code, string Message, long? Delta = null);
