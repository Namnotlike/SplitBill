namespace SplitBill.Application.Settlement;

/// <summary>
/// Số dư ròng của một thành viên. Net &gt; 0: chủ nợ (người khác phải trả cho họ).
/// Net &lt; 0: con nợ. Bất biến: Σ net == 0 (CLAUDE.md mục 6.1).
/// </summary>
public sealed record MemberBalance(Guid MemberId, long Net);
