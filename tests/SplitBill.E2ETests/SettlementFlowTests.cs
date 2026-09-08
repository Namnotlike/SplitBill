using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace SplitBill.E2ETests;

/// <summary>
/// E2E cho luồng thanh toán khép kín "ghi nhận → xác nhận" (mục 8 CLAUDE.md
/// "POST /settlements/{id}/confirm — Người NHẬN xác nhận") — phần duy nhất của bài toán cốt lõi
/// (mục 1: "hệ thống phải tính ra danh sách lượt chuyển tiền tối thiểu sao cho mọi người đều thu/chi
/// đúng số tiền của mình") mà <see cref="GroupExpenseFlowTests"/> CHƯA phủ tới — test đó dừng lại ở
/// việc XEM kế hoạch thanh toán, không thực sự ghi nhận/xác nhận một giao dịch nào. Test này đi hết cả
/// vòng đời `Settlement` (Pending → Confirmed) bằng 2 tài khoản thật, xác nhận số dư thật sự về 0 sau
/// khi xác nhận — đúng bất biến "Σ net = 0" (mục 6.1).
///
/// Đồng thời phủ luôn luồng "tham gia nhóm qua link chia sẻ" (mục 15.6) — đây là cách DUY NHẤT trên
/// Web UI để 1 tài khoản thật (không phải khách vãng lai) trở thành thành viên nhóm (mục 13.4: form
/// "Thêm thành viên" trên Details chỉ hỗ trợ khách vãng lai), nên cần thiết để có 2 member đều đăng
/// nhập được và tự thao tác Xác nhận/Ghi nhận từ đúng tài khoản của mình.
/// </summary>
[Collection("E2E")]
public sealed class SettlementFlowTests(E2EFixture fixture)
{
    [Fact]
    public async Task RecordThenConfirmSettlement_BothMembersEndUpSettled()
    {
        var ownerEmail = $"e2e-{Guid.NewGuid():N}@example.com";
        var payerEmail = $"e2e-{Guid.NewGuid():N}@example.com";

        // ===== Owner: đăng ký + tạo nhóm =====
        await using var ownerContext = await fixture.NewContextAsync();
        var ownerPage = await ownerContext.NewPageAsync();

        await ownerPage.GotoAsync(fixture.WebBaseUrl + "/Account/Register");
        await ownerPage.FillAsync("#Input_DisplayName", "Settlement Owner");
        await ownerPage.FillAsync("#Input_Email", ownerEmail);
        await ownerPage.FillAsync("#Input_Password", "Passw0rd123");
        await ownerPage.ClickAsync("button[type=submit]");
        await Assertions.Expect(ownerPage).ToHaveURLAsync(new Regex(@"/Groups$"));

        await ownerPage.FillAsync("#NewGroup_Name", "Settlement Trip");
        await ownerPage.ClickAsync("button:has-text('Tạo nhóm')");
        await Assertions.Expect(ownerPage).ToHaveURLAsync(new Regex(@"/Groups/Details/"));
        var groupId = ownerPage.Url.Split('/').Last();

        // Link chia sẻ nằm trong ô <input readonly> duy nhất trên trang Details (mục 15.6).
        var shareLink = await ownerPage.Locator("input[readonly]").First.InputValueAsync();

        // ===== Payer: đăng ký (context/cookie riêng), rồi tham gia qua link chia sẻ =====
        await using var payerContext = await fixture.NewContextAsync();
        var payerPage = await payerContext.NewPageAsync();

        await payerPage.GotoAsync(fixture.WebBaseUrl + "/Account/Register");
        await payerPage.FillAsync("#Input_DisplayName", "Settlement Payer");
        await payerPage.FillAsync("#Input_Email", payerEmail);
        await payerPage.FillAsync("#Input_Password", "Passw0rd123");
        await payerPage.ClickAsync("button[type=submit]");
        await Assertions.Expect(payerPage).ToHaveURLAsync(new Regex(@"/Groups$"));

        await payerPage.GotoAsync(shareLink);
        await payerPage.ClickAsync("button:has-text('Tham gia nhóm này')");
        await Assertions.Expect(payerPage).ToHaveURLAsync(new Regex(@"/Groups/Details/"));
        await Assertions.Expect(payerPage.Locator(".alert-success")).ToContainTextAsync("Bạn đã tham gia nhóm");

        // ===== Owner: tạo khoản chi 100.000đ, tự ứng toàn bộ, chia đều 2 người =====
        await ownerPage.GotoAsync($"{fixture.WebBaseUrl}/Expenses/Create/{groupId}");
        await ownerPage.FillAsync("#Input_Title", "Settlement Dinner");
        await ownerPage.FillAsync("#Input_TotalAmount", "100000");
        // Cùng giả định như GroupExpenseFlowTests (xem ⚠️ ở đó): .First là Owner vì EF Core InMemory
        // hiện trả Group.Members đúng thứ tự tạo/tham gia — không phải hợp đồng được đảm bảo, nhưng sai
        // thì lộ ngay (Payer sẽ không thấy nút "Tôi đã chuyển khoản này" ở bước dưới vì đổi vai trò
        // nợ/chủ nợ, ClickAsync throw ngay chứ không âm thầm pass sai).
        await ownerPage.Locator("input[id^='Input_Rows_'][id$='__PayerAmount']").First.FillAsync("100000");
        await ownerPage.ClickAsync("button:has-text('Lưu khoản chi')");
        await Assertions.Expect(ownerPage).ToHaveURLAsync(new Regex($@"/Expenses/{Regex.Escape(groupId)}$"));

        // ===== Payer: xem kế hoạch thanh toán, tự ghi nhận đã chuyển (mình là FromMemberId) =====
        await payerPage.GotoAsync($"{fixture.WebBaseUrl}/Groups/SettlementPlan/{groupId}");
        await Assertions.Expect(payerPage.Locator("body")).ToContainTextAsync("Settlement Owner");
        await payerPage.ClickAsync("button:has-text('Tôi đã chuyển khoản này')");
        await Assertions.Expect(payerPage.Locator(".alert-success")).ToContainTextAsync("Đã ghi nhận, chờ người nhận xác nhận");

        // ===== Owner: xác nhận đã nhận (mình là ToMemberId) =====
        await ownerPage.GotoAsync($"{fixture.WebBaseUrl}/Groups/SettlementPlan/{groupId}");
        await ownerPage.ClickAsync("button:has-text('Xác nhận')");
        await Assertions.Expect(ownerPage.Locator(".alert-success")).ToContainTextAsync("Đã xác nhận thanh toán");

        // ===== Cả hai: số dư về đúng 0 sau khi Settlement Confirmed (bất biến Σ net = 0, mục 6.1) =====
        await ownerPage.GotoAsync($"{fixture.WebBaseUrl}/Groups/Balances/{groupId}");
        var balancesText = await ownerPage.Locator("body").TextContentAsync();
        balancesText.Should().NotContain("+50.000đ");
        balancesText.Should().NotContain("-50.000đ");
        Regex.Matches(balancesText!, "Đã cân bằng").Count.Should().Be(2);

        // Kế hoạch thanh toán không còn giao dịch nào cần thực hiện nữa.
        await ownerPage.GotoAsync($"{fixture.WebBaseUrl}/Groups/SettlementPlan/{groupId}");
        (await ownerPage.Locator("body").TextContentAsync())!.Should().Contain("Mọi người đã cân bằng");
    }
}
