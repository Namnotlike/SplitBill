using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace SplitBill.E2ETests;

/// <summary>
/// E2E cho luồng đăng ký/đăng nhập/đăng xuất — lái 1 trình duyệt Chromium thật qua toàn bộ chồng
/// công nghệ thật (Chromium → Kestrel của SplitBill.Web → HTTP → Kestrel của SplitBill.Api → EF Core),
/// khác hẳn 3 project test hiện có: SplitBill.UnitTests chỉ test thuật toán thuần, SplitBill.
/// IntegrationTests gọi thẳng Application service (bỏ qua toàn bộ tầng Controller/HTTP/Razor Pages —
/// xem TestHarness.cs), SplitBill.Web.Tests không render Razor View thật (CLAUDE.md mục 10b). Đây là
/// lớp test DUY NHẤT xác nhận cookie đăng nhập (BFF, mục 10b) và việc render HTML thật hoạt động đúng
/// end-to-end.
/// </summary>
[Collection("E2E")]
public sealed class AuthFlowTests(E2EFixture fixture)
{
    [Fact]
    public async Task Register_RedirectsToGroups_ThenLogout_RedirectsToLogin()
    {
        var email = $"e2e-{Guid.NewGuid():N}@example.com";
        await using var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.WebBaseUrl + "/Account/Register");
        await page.FillAsync("#Input_DisplayName", "E2E Tester");
        await page.FillAsync("#Input_Email", email);
        await page.FillAsync("#Input_Password", "Passw0rd123");
        await page.ClickAsync("button[type=submit]");

        // Đăng ký thành công -> RegisterModel.OnPostAsync redirect về /Groups/Index (khi không có
        // ReturnUrl) — xem Register.cshtml.cs. ⚠️ KHÔNG dùng page.WaitForURLAsync(...) ở đây: ClickAsync
        // trên nút submit đã tự chờ xong điều hướng phát sinh bởi chính cú click đó trước khi trả về, nên
        // gọi WaitForURLAsync SAU ClickAsync là chờ một sự kiện điều hướng TƯƠNG LAI không bao giờ xảy ra
        // nữa (điều hướng đã xảy ra RỒI) — luôn timeout 30s dù luồng thật hoàn toàn thành công (xác nhận
        // qua log Serilog: mọi request server-to-server auth/register, users/me, groups,
        // notifications/unread-count đều 200). Assertions.Expect(...).ToHaveURLAsync(...) đúng hơn vì nó
        // kiểm tra URL HIỆN TẠI trước, chỉ retry nếu chưa khớp — an toàn với cả 2 tình huống (đã điều
        // hướng xong hoặc đang chờ).
        // ⚠️ Route thật là "/Groups" KHÔNG PHẢI "/Groups/Index" — quy ước Razor Pages tự lược bỏ hậu
        // tố "Index" khỏi route của trang gốc trong 1 thư mục (đã xác nhận qua chạy thực tế: page.Url
        // trả về đúng ".../Groups", không có "/Index"). asp-page="/Groups/Index" trong .cshtml vẫn
        // dùng ĐÚNG TÊN TRANG (không phải route) nên không bị ảnh hưởng — chỉ URL cuối cùng là rút gọn.
        await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/Groups$"));
        (await page.TextContentAsync("h1"))!.Should().Contain("Nhóm của tôi");

        // Đăng xuất qua form POST ẩn trên navbar (_Layout.cshtml) — không phải link GET.
        // LogoutModel.OnPostAsync redirect về "/Index" (trang chủ) sau khi SignOutAsync, KHÔNG phải
        // trang Login — xác nhận qua đọc trực tiếp Logout.cshtml.cs.
        var logoutForm = page.Locator("form[action*='/Account/Logout']");
        await logoutForm.Locator("button[type=submit]").ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/$"));
        (await page.Locator("form[action*='/Account/Logout']").CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsError_DoesNotRedirect()
    {
        var email = $"e2e-{Guid.NewGuid():N}@example.com";
        await using var registerContext = await fixture.NewContextAsync();
        var registerPage = await registerContext.NewPageAsync();
        await registerPage.GotoAsync(fixture.WebBaseUrl + "/Account/Register");
        await registerPage.FillAsync("#Input_DisplayName", "E2E Tester 2");
        await registerPage.FillAsync("#Input_Email", email);
        await registerPage.FillAsync("#Input_Password", "Passw0rd123");
        await registerPage.ClickAsync("button[type=submit]");
        await Assertions.Expect(registerPage).ToHaveURLAsync(new Regex(@"/Groups$"));

        // Đăng nhập lại (context MỚI, không mang cookie của lần đăng ký) bằng sai mật khẩu — CLAUDE.md
        // mục 8: "Sai email/password trả 401 ... không tiết lộ email có tồn tại hay không", Web
        // (LoginModel) hiển thị đúng 1 câu chung chung "Email hoặc mật khẩu không đúng."
        await using var loginContext = await fixture.NewContextAsync();
        var loginPage = await loginContext.NewPageAsync();
        await loginPage.GotoAsync(fixture.WebBaseUrl + "/Account/Login");
        await loginPage.FillAsync("#Input_Email", email);
        await loginPage.FillAsync("#Input_Password", "wrong-password");
        await loginPage.ClickAsync("button[type=submit]");

        await Assertions.Expect(loginPage.Locator(".alert-danger")).ToContainTextAsync("Email hoặc mật khẩu không đúng");
        loginPage.Url.Should().Contain("/Account/Login");
    }
}
