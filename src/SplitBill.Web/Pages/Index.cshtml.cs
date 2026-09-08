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

    // "Ai đang nợ tôi" xuyên nhóm (CLAUDE.md mục 20) — cùng nguyên tắc chịu lỗi im lặng như trên.
    public IReadOnlyList<CounterpartyBalanceDto> CounterpartyBalances { get; set; } = Array.Empty<CounterpartyBalanceDto>();

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
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            // Trang chủ vẫn phải hiển thị được dù widget tổng quan lỗi (vd token vừa hết hạn, hoặc
            // chính SplitBill.Api không phản hồi được — HttpRequestException, khác ApiException vốn
            // chỉ bắt lỗi HTTP có response) — không TempData/redirect, chỉ đơn giản ẩn widget. Phát
            // hiện qua verify sống PWA (CLAUDE.md mục 23.4): tắt Api rồi tải trang chủ ra lỗi 500 chưa
            // xử lý vì trước đây chỉ bắt ApiException.
        }

        try
        {
            CounterpartyBalances = await _apiClient.GetMyCounterpartyBalancesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
        }
    }
}
