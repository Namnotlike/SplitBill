namespace SplitBill.Application.Users;

/// <summary>Dashboard cá nhân nâng cao ở trang chủ (CLAUDE.md mục 25.5, bổ sung 2026-09-09) — khác
/// widget "Tổng quan số dư" (mục 15.4, chỉ số dư từng nhóm) và "Ai đang nợ tôi" (mục 20, chỉ nợ xuyên
/// nhóm): dashboard này trả lời "có gì cần tôi chú ý/xử lý ngay" (settlement đang chờ mình xác nhận)
/// và "gần đây nhóm nào có gì mới" (hoạt động xuyên nhóm) — 2 câu hỏi mà 2 widget kia không trả lời
/// được.</summary>
public sealed record PersonalDashboardDto(
    int TotalActiveGroups,
    int ExpensesThisMonth,
    IReadOnlyList<PendingSettlementToConfirmDto> PendingSettlementsToConfirm,
    IReadOnlyList<DashboardActivityDto> RecentActivity);

/// <summary>1 settlement đang ở trạng thái Pending mà caller là ToMemberId (người NHẬN, phải xác nhận
/// hoặc từ chối — CLAUDE.md mục 8) — hành động cụ thể caller có thể làm ngay, khác dữ liệu chỉ để xem.</summary>
public sealed record PendingSettlementToConfirmDto(
    Guid SettlementId,
    Guid GroupId,
    string GroupName,
    string Currency,
    string FromMemberName,
    long Amount,
    DateTimeOffset CreatedAt);

/// <summary>1 dòng hoạt động — tái dùng nguyên <c>AuditLogDto.Summary</c> đã dựng sẵn ở tầng
/// Application cho Timeline từng nhóm (CLAUDE.md mục 15.5), chỉ thêm GroupName để phân biệt nguồn khi
/// gộp xuyên nhiều nhóm.</summary>
public sealed record DashboardActivityDto(Guid GroupId, string GroupName, string Summary, DateTimeOffset CreatedAt);
