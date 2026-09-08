using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace SplitBill.E2ETests;

/// <summary>
/// E2E cho luồng lõi "tạo nhóm → thêm thành viên → ghi khoản chi → xem số dư/kế hoạch thanh toán" —
/// đúng bài toán cốt lõi của SplitBill (CLAUDE.md mục 1), lái qua trình duyệt thật để xác nhận
/// Razor Pages (SplitBill.Web) và Controller/Application/EF Core (SplitBill.Api) khớp đúng shape dữ
/// liệu với nhau qua HTTP thật — điều mà SplitBill.IntegrationTests (gọi thẳng service, bỏ qua tầng
/// HTTP) và SplitBill.UnitTests (chỉ test thuật toán thuần) không phủ được.
/// </summary>
[Collection("E2E")]
public sealed class GroupExpenseFlowTests(E2EFixture fixture)
{
    [Fact]
    public async Task CreateGroup_AddExpense_ShowsCorrectBalanceAndSettlementPlan()
    {
        var email = $"e2e-{Guid.NewGuid():N}@example.com";
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        // ===== Đăng ký =====
        await page.GotoAsync(fixture.WebBaseUrl + "/Account/Register");
        await page.FillAsync("#Input_DisplayName", "Owner Tester");
        await page.FillAsync("#Input_Email", email);
        await page.FillAsync("#Input_Password", "Passw0rd123");
        await page.ClickAsync("button[type=submit]");
        // ⚠️ Không dùng page.WaitForURLAsync sau ClickAsync — xem giải thích chi tiết ở AuthFlowTests.cs
        // (ClickAsync đã tự chờ xong điều hướng do chính nó gây ra; gọi WaitForURLAsync sau đó là chờ 1
        // sự kiện tương lai không còn xảy ra nữa, luôn timeout dù luồng thật đã thành công).
        // Route thật là "/Groups" (Razor Pages tự lược "Index" khỏi route trang gốc 1 thư mục — xem
        // giải thích ở AuthFlowTests.cs), không phải "/Groups/Index".
        await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/Groups$"));

        // ===== Tạo nhóm (VND mặc định) =====
        await page.FillAsync("#NewGroup_Name", "E2E Trip");
        await page.ClickAsync("button:has-text('Tạo nhóm')");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/Groups/Details/"));
        (await page.TextContentAsync("h1"))!.Should().Contain("E2E Trip");

        // ===== Thêm 1 thành viên khách vãng lai =====
        await page.FillAsync("#NewMember_DisplayName", "Guest Friend");
        await page.ClickAsync("button:has-text('+ Thêm')");
        await Assertions.Expect(page.Locator("table")).ToContainTextAsync("Guest Friend");

        var groupUrl = page.Url;
        var groupId = groupUrl.Split('/').Last();

        // ===== Tạo khoản chi: 100.000đ, Owner ứng toàn bộ, chia đều 2 người =====
        await page.GotoAsync($"{fixture.WebBaseUrl}/Expenses/Create/{groupId}");
        await page.FillAsync("#Input_Title", "Test Dinner");
        await page.FillAsync("#Input_TotalAmount", "100000");
        // Input.Rows được CreateModel.OnGetAsync khởi tạo sẵn EqualParticipant = true cho MỌI thành
        // viên (xem Create.cshtml.cs) — chỉ cần điền số tiền đã ứng cho đúng 1 dòng (Owner), không
        // cần đụng tới checkbox "Tham gia?" của chế độ Equal mặc định.
        // ⚠️ .First giả định Group.Members[0] (dòng đầu DOM) là Owner — đúng thực tế trên EF Core
        // InMemory (không ORDER BY nào can thiệp, trả về đúng thứ tự insert) nhưng đây KHÔNG phải hợp
        // đồng được đảm bảo của EF Core, chỉ là hành vi hiện tại của provider. Nếu giả định này sai:
        // thất bại sẽ LỘ RÕ NGAY (điền nhầm PayerAmount cho Guest thay vì Owner khiến bước assert sau
        // — "+50.000đ"/"-50.000đ" đúng người — sai ngay lập tức), không âm thầm pass sai.
        await page.Locator("input[id^='Input_Rows_'][id$='__PayerAmount']").First.FillAsync("100000");
        await page.ClickAsync("button:has-text('Lưu khoản chi')");
        // Cùng quy ước lược "Index": route thật của Expenses/Index.cshtml (@page "{groupId:guid}") là
        // "/Expenses/{groupId}", không phải "/Expenses/Index/{groupId}".
        await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/Expenses/{Regex.Escape(groupId)}$"));
        (await page.Locator(".alert-success").TextContentAsync())!.Should().Contain("Đã thêm khoản chi");

        // ===== Số dư: Owner +50.000đ, Guest -50.000đ (chia đều 100.000đ / 2 người) =====
        await page.GotoAsync($"{fixture.WebBaseUrl}/Groups/Balances/{groupId}");
        var balancesText = await page.Locator("body").TextContentAsync();
        balancesText.Should().Contain("+50.000đ");
        balancesText.Should().Contain("-50.000đ");

        // ===== Kế hoạch thanh toán: đúng 1 giao dịch Guest Friend -> Owner Tester =====
        await page.GotoAsync($"{fixture.WebBaseUrl}/Groups/SettlementPlan/{groupId}");
        var planText = await page.Locator("body").TextContentAsync();
        planText.Should().Contain("Guest Friend");
        planText.Should().Contain("chuyển cho");
        planText.Should().Contain("Owner Tester");
        planText.Should().Contain("50.000đ");
    }
}
