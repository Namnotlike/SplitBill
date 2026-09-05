using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Settlements;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages;

public class IndexModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public IndexModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    // Bảng tổng quan cá nhân (CLAUDE.md mục 15.4) — chỉ tải khi đã đăng nhập. Rỗng nếu chưa vào
    // nhóm nào, hoặc nếu gọi API lỗi (không để lỗi ở widget phụ này chặn cả trang chủ).
    public IReadOnlyList<PersonalGroupBalanceDto> MyOverview { get; set; } = Array.Empty<PersonalGroupBalanceDto>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        try
        {
            MyOverview = await _apiClient.GetMyBalancesOverviewAsync(cancellationToken);
        }
        catch (ApiException)
        {
            // Trang chủ vẫn phải hiển thị được dù widget tổng quan lỗi (vd token vừa hết hạn) —
            // không TempData/redirect, chỉ đơn giản ẩn widget.
        }
    }
}
