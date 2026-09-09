using Microsoft.AspNetCore.Mvc.RazorPages;
using SplitBill.Application.Users;
using SplitBill.Web.Services;

namespace SplitBill.Web.Pages;

/// <summary>CLAUDE.md mục 25.8 — Tìm kiếm xuyên nhóm. Trang gốc (không thuộc `Groups/`) vì tìm kiếm
/// quét TOÀN BỘ nhóm caller đang tham gia, không thuộc về 1 nhóm cụ thể nào — cùng vị trí với
/// `Index.cshtml`/`SetLanguage.cshtml`.</summary>
public class SearchModel : PageModel
{
    private readonly SplitBillApiClient _apiClient;

    public SearchModel(SplitBillApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string? Query { get; set; }
    public GlobalSearchResultDto Result { get; set; } = new([], []);
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(string? q, CancellationToken cancellationToken)
    {
        Query = q;
        if (string.IsNullOrWhiteSpace(q))
        {
            return;
        }

        try
        {
            Result = await _apiClient.GlobalSearchAsync(q, cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            // Cùng nguyên tắc chịu lỗi im lặng như các widget cá nhân khác (mục 23.4) — nhưng đây là
            // TRANG CHÍNH (không phải widget phụ trên trang khác) nên vẫn cần hiện thông báo lỗi rõ
            // ràng cho người dùng thay vì chỉ ẩn lặng lẽ.
            ErrorMessage = "Không tìm kiếm được lúc này, vui lòng thử lại.";
        }
    }
}
