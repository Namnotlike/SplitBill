using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SplitBill.Application.Expenses;
using SplitBill.Web.Pages.Expenses;
using SplitBill.Web.Services;
using Xunit;

namespace SplitBill.Web.Tests;

/// <summary>
/// Test cho <see cref="EditModel.OnGetAsync"/> — bổ sung 2026-09-05 cùng đợt sửa lỗi: form Edit
/// trước đó luôn suy ngược trọng số/%/danh sách món ăn từ <c>ExpenseSplit.Amount</c> cuối cùng dù
/// API đã có sẵn <c>ExpenseDto.SplitConfigJson</c> chứa đúng input gốc — nay phải ưu tiên dùng
/// SplitConfigJson khi có.
/// </summary>
public sealed class EditModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid ExpenseId = Guid.NewGuid();
    private static readonly Guid Nam = Guid.NewGuid();
    private static readonly Guid Binh = Guid.NewGuid();

    private static EditModel CreateModel(object expenseBody)
    {
        var handler = new FakeHttpMessageHandler(req =>
        {
            object body = req.RequestUri!.AbsolutePath.Contains("/groups/")
                ? new
                {
                    id = GroupId,
                    name = "Du lich",
                    description = (string?)null,
                    type = "OneTime",
                    currency = "VND",
                    simplifyDebts = true,
                    isArchived = false,
                    shareToken = "token",
                    members = new object[]
                    {
                        new { id = Nam, userId = (Guid?)null, displayName = "Nam", role = "Owner", isActive = true },
                        new { id = Binh, userId = (Guid?)null, displayName = "Binh", role = "Member", isActive = true },
                    },
                }
                : expenseBody;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/v1/") };
        var apiClient = new SplitBillApiClient(httpClient);

        var model = new EditModel(apiClient) { ExpenseId = ExpenseId };
        var httpContext = WebTestHelpers.CreateHttpContext();
        WebTestHelpers.AttachPageContext(model, httpContext);
        return model;
    }

    private static object BaseExpenseBody(string splitMode, object? splits, string? splitConfigJson) => new
    {
        id = ExpenseId,
        groupId = GroupId,
        title = "An toi",
        totalAmount = 100_000,
        extraFeeAmount = 0,
        splitMode,
        note = (string?)null,
        receiptImageUrl = (string?)null,
        occurredAt = DateTimeOffset.UtcNow,
        payers = new[] { new { memberId = Nam, amount = 100_000L } },
        splits,
        rowVersion = Convert.ToBase64String([1, 2, 3, 4, 5, 6, 7, 8]),
        splitConfigJson = splitConfigJson,
    };

    [Fact]
    public async Task OnGetAsync_SharesMode_RestoresOriginalWeightsFromSplitConfigJson()
    {
        // Amount cuối cùng (25k/75k) không tỉ lệ đúng với weight gốc (1:1) do có ExtraFee/làm tròn ở
        // ví dụ này — cố tình để phân biệt rõ "suy ngược từ Amount" (sai) với "đọc từ SplitConfigJson"
        // (đúng): nếu code còn dùng nhánh cũ, ShareWeight sẽ ra tỉ lệ 25:75 thay vì 1:1 như đã lưu.
        // Lưu ý: SplitConfigJson thật do ExpenseService ghi bằng JsonSerializer.Serialize KHÔNG kèm
        // options (PascalCase mặc định) — khác với JsonOptions (camelCase) dùng để giả lập response
        // HTTP ở CreateModel; phải dựng chuỗi này bằng options mặc định để mô phỏng đúng dữ liệu thật.
        var splitConfigJson = JsonSerializer.Serialize(new SplitConfigInput(
            Shares: [new SharesInput(Nam, 1), new SharesInput(Binh, 1)]));
        var body = BaseExpenseBody("Shares", new[]
        {
            new { memberId = Nam, amount = 25_000L },
            new { memberId = Binh, amount = 75_000L },
        }, splitConfigJson);

        var model = CreateModel(body);
        await model.OnGetAsync(CancellationToken.None);

        var namRow = model.Input.Rows.Single(r => r.MemberId == Nam);
        var binhRow = model.Input.Rows.Single(r => r.MemberId == Binh);
        namRow.ShareWeight.Should().Be(1m);
        binhRow.ShareWeight.Should().Be(1m);
    }

    [Fact]
    public async Task OnGetAsync_SharesMode_NoSplitConfigJson_FallsBackToAmountAsWeight()
    {
        var body = BaseExpenseBody("Shares", new[]
        {
            new { memberId = Nam, amount = 25_000L },
            new { memberId = Binh, amount = 75_000L },
        }, splitConfigJson: null);

        var model = CreateModel(body);
        await model.OnGetAsync(CancellationToken.None);

        model.Input.Rows.Single(r => r.MemberId == Nam).ShareWeight.Should().Be(25_000m);
        model.Input.Rows.Single(r => r.MemberId == Binh).ShareWeight.Should().Be(75_000m);
    }

    [Fact]
    public async Task OnGetAsync_ItemizedMode_RestoresItemsAndConsumersFromSplitConfigJson()
    {
        var splitConfigJson = JsonSerializer.Serialize(new SplitConfigInput(
            Items:
            [
                new ItemizedInput("Lau", 100_000, [Nam, Binh]),
                new ItemizedInput("Nuoc ngot", 20_000, [Binh]),
            ]));
        var body = BaseExpenseBody("Itemized", new[]
        {
            new { memberId = Nam, amount = 60_000L },
            new { memberId = Binh, amount = 60_000L },
        }, splitConfigJson);

        var model = CreateModel(body);
        await model.OnGetAsync(CancellationToken.None);

        model.Input.Items.Should().HaveCount(2);
        model.Input.Items.Should().ContainSingle(i => i.Name == "Lau" && i.Price == 100_000 && i.ConsumerMemberIds.Count == 2);
        model.Input.Items.Should().ContainSingle(i => i.Name == "Nuoc ngot" && i.Price == 20_000 && i.ConsumerMemberIds.Single() == Binh);
    }

    [Fact]
    public async Task OnGetAsync_ItemizedMode_NoSplitConfigJson_ItemsEmpty()
    {
        var body = BaseExpenseBody("Itemized", new[]
        {
            new { memberId = Nam, amount = 60_000L },
            new { memberId = Binh, amount = 60_000L },
        }, splitConfigJson: null);

        var model = CreateModel(body);
        await model.OnGetAsync(CancellationToken.None);

        model.Input.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task OnGetAsync_MalformedSplitConfigJson_FallsBackGracefullyWithoutThrowing()
    {
        var body = BaseExpenseBody("Shares", new[]
        {
            new { memberId = Nam, amount = 50_000L },
            new { memberId = Binh, amount = 50_000L },
        }, splitConfigJson: "{not valid json");

        var model = CreateModel(body);
        var act = async () => await model.OnGetAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        model.Input.Rows.Single(r => r.MemberId == Nam).ShareWeight.Should().Be(50_000m);
    }
}
