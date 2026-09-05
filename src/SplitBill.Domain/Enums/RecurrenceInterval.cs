namespace SplitBill.Domain.Enums;

/// <summary>Chu kỳ lặp lại của khoản chi định kỳ (CLAUDE.md mục 15.7, bổ sung 2026-09-05).</summary>
public enum RecurrenceInterval
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
}
