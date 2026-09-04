namespace SplitBill.Application.Settlement;

/// <summary>
/// CLAUDE.md mục 6.4 — "ràng buộc mềm": khi có nhiều phương án cùng số giao dịch tối thiểu, chấm
/// điểm từng phương án và chọn phương án tốt nhất.
/// </summary>
public interface ISettlementRanker
{
    /// <param name="candidates">Các phương án ứng viên (không nhất thiết cùng số giao dịch — ranker
    /// tự lọc về nhóm có số giao dịch tối thiểu trước khi chấm điểm).</param>
    /// <param name="pastPairs">Các cặp thành viên đã từng có Settlement với nhau trong nhóm (không
    /// phân biệt chiều — "với nhau").</param>
    IReadOnlyList<SettlementTransaction> SelectBest(
        IReadOnlyList<IReadOnlyList<SettlementTransaction>> candidates,
        IReadOnlyCollection<(Guid MemberA, Guid MemberB)> pastPairs);
}
