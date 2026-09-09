using System.Security.Claims;
using FluentAssertions;
using SplitBill.Web.Pages;
using SplitBill.Web.Services;
using Xunit;

namespace SplitBill.Web.Tests;

/// <summary>
/// Test cho <see cref="IndexModel.OnGetAsync"/> — bổ sung 2026-09-08, phát hiện khi verify sống tính
/// năng PWA (CLAUDE.md mục 23.4): trước đây khối try/catch quanh 2 lời gọi API tổng quan cá nhân
/// (mục 15.4, mục 20) chỉ bắt <see cref="ApiException"/> (lỗi HTTP có response) — khi chính
/// SplitBill.Api không phản hồi được (mất mạng, Api sập...), HttpClient ném
/// <see cref="HttpRequestException"/> KHÔNG bị bắt, làm sập cả trang chủ với lỗi 500 chưa xử lý thay
/// vì chỉ ẩn lặng lẽ 2 widget như tài liệu đã mô tả. Đã sửa bằng cách bắt thêm HttpRequestException
/// (cùng mẫu tại 5 điểm gọi API "best-effort" khác trong SplitBill.Web — xem CLAUDE.md mục 23.4).
/// </summary>
public sealed class IndexModelTests
{
    private static IndexModel CreateModel(Func<HttpRequestMessage, HttpResponseMessage> responder, bool authenticated)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/v1/") };
        var apiClient = new SplitBillApiClient(httpClient, WebTestHelpers.EmptyConfiguration());

        var model = new IndexModel(apiClient);
        var httpContext = WebTestHelpers.CreateHttpContext();
        if (authenticated)
        {
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], authenticationType: "Test"));
        }

        WebTestHelpers.AttachPageContext(model, httpContext);
        return model;
    }

    [Fact]
    public async Task OnGetAsync_ApiUnreachable_DoesNotThrow_LeavesWidgetsEmpty()
    {
        // Mô phỏng đúng lỗi thật khi SplitBill.Api không chạy được (connection refused) — không trả
        // HttpResponseMessage nào cả, ném thẳng HttpRequestException như HttpClient thật sẽ làm.
        var model = CreateModel(_ => throw new HttpRequestException("No connection could be made"), authenticated: true);

        var act = async () => await model.OnGetAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        model.MyOverview.Should().BeEmpty();
        model.CounterpartyBalances.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_NotAuthenticated_DoesNotCallApi()
    {
        var called = false;
        var model = CreateModel(_ =>
        {
            called = true;
            throw new HttpRequestException("should not be called");
        }, authenticated: false);

        await model.OnGetAsync(CancellationToken.None);

        called.Should().BeFalse();
        model.MyOverview.Should().BeEmpty();
        model.CounterpartyBalances.Should().BeEmpty();
    }
}
