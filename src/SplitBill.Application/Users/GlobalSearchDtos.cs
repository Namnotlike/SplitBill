namespace SplitBill.Application.Users;

/// <summary>Tìm kiếm xuyên nhóm (CLAUDE.md mục 25.8) — 1 kết quả nhóm khớp tên.</summary>
public sealed record GroupSearchHitDto(Guid GroupId, string GroupName, string Type, string Currency);

/// <summary>1 kết quả khoản chi khớp tiêu đề, kèm sẵn tên/tiền tệ nhóm chứa nó để hiển thị mà không
/// cần gọi thêm API nào khác.</summary>
public sealed record ExpenseSearchHitDto(
    Guid ExpenseId, Guid GroupId, string GroupName, string Title, long TotalAmount, string Currency, DateTimeOffset OccurredAt);

public sealed record GlobalSearchResultDto(IReadOnlyList<GroupSearchHitDto> Groups, IReadOnlyList<ExpenseSearchHitDto> Expenses);
