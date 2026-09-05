using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Application.Groups;
using SplitBill.Web.Services;
using Xunit;

namespace SplitBill.Web.Tests;

public sealed class SplitBillApiClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static (SplitBillApiClient Client, FakeHttpMessageHandler Handler) CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/v1/") };
        return (new SplitBillApiClient(httpClient), handler);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task GetMyGroupsAsync_ParsesGroupList()
    {
        var groups = new[] { new GroupSummaryDto(Guid.NewGuid(), "Du lich", "OneTime", false, "VND") };
        var (client, _) = CreateClient(_ => JsonResponse(HttpStatusCode.OK, groups));

        var result = await client.GetMyGroupsAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Du lich");
    }

    [Fact]
    public async Task CreateGroupAsync_SendsCamelCaseJsonBody()
    {
        var groupId = Guid.NewGuid();
        var (client, handler) = CreateClient(_ => JsonResponse(HttpStatusCode.Created,
            new GroupDto(groupId, "Du lich", null, "OneTime", "VND", true, false, "abc123", [])));

        await client.CreateGroupAsync(new CreateGroupRequest("Du lich", null, "OneTime", "VND"), CancellationToken.None);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/api/v1/groups");
        handler.LastRequestBody.Should().Contain("\"name\":\"Du lich\""); // camelCase, không phải PascalCase
    }

    [Fact]
    public async Task ErrorResponse_ThrowsApiExceptionWithErrorCodeAndStatus()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"title":"Email đã được đăng ký.","status":409,"errorCode":"EMAIL_ALREADY_REGISTERED"}""",
                Encoding.UTF8, "application/problem+json"),
        });

        var act = () => client.GetMyGroupsAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.ErrorCode.Should().Be("EMAIL_ALREADY_REGISTERED");
        ex.Which.Message.Should().Be("Email đã được đăng ký.");
    }

    [Fact]
    public async Task ErrorResponse_NonJsonBody_StillThrowsWithFallbackMessage()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>server crashed</html>", Encoding.UTF8, "text/html"),
        });

        var act = () => client.GetMyGroupsAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GetExpensesAsync_BuildsCorrectQueryString()
    {
        var groupId = Guid.NewGuid();
        var (client, handler) = CreateClient(_ => JsonResponse(HttpStatusCode.OK,
            new { items = Array.Empty<object>(), page = 2, pageSize = 20, totalCount = 0 }));

        await client.GetExpensesAsync(groupId, 2, 20, ExpenseFilter.Empty, CancellationToken.None);

        handler.LastRequest!.RequestUri!.Query.Should().Be("?page=2&pageSize=20");
    }

    // CLAUDE.md mục 15.2 — Tìm kiếm/lọc khoản chi, bổ sung 2026-09-05.
    [Fact]
    public async Task GetExpensesAsync_WithFilter_AppendsOnlyProvidedFields()
    {
        var groupId = Guid.NewGuid();
        var payerMemberId = Guid.NewGuid();
        var (client, handler) = CreateClient(_ => JsonResponse(HttpStatusCode.OK,
            new { items = Array.Empty<object>(), page = 1, pageSize = 20, totalCount = 0 }));

        var filter = new ExpenseFilter(Title: "ăn tối", PayerMemberId: payerMemberId, MinAmount: 10_000, MaxAmount: 500_000);
        await client.GetExpensesAsync(groupId, 1, 20, filter, CancellationToken.None);

        var query = handler.LastRequest!.RequestUri!.Query;
        query.Should().Contain("page=1").And.Contain("pageSize=20");
        query.Should().Contain($"payerMemberId={payerMemberId}");
        query.Should().Contain("minAmount=10000").And.Contain("maxAmount=500000");
        query.Should().Contain("title=");
        query.Should().NotContain("fromDate=").And.NotContain("toDate=");
    }
}
