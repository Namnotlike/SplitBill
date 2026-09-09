namespace SplitBill.Application.Users;

public interface IUserDashboardService
{
    /// <summary>Dashboard cá nhân nâng cao (CLAUDE.md mục 25.5) — luôn thành công với callerUserId hợp
    /// lệ, không ném lỗi "không phải thành viên" nào (khác các service theo-nhóm khác) vì đây là dữ
    /// liệu tổng hợp theo USER, không theo 1 nhóm cụ thể — giống mẫu <c>BalanceService.GetMyOverviewAsync</c>.</summary>
    Task<PersonalDashboardDto> GetDashboardAsync(Guid callerUserId, CancellationToken cancellationToken);
}
