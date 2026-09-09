using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Localization;
using SplitBill.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== Đa ngôn ngữ (CLAUDE.md mục 22, bổ sung 2026-09-07) =====
// Tiếng Việt là văn bản GỐC viết thẳng trong .cshtml (dùng làm resource KEY luôn — không cần file
// .resx riêng cho "vi", vì IStringLocalizer tự fallback về đúng key khi không tìm thấy bản dịch).
// Chỉ cần cung cấp file .resx cho "en" (bản dịch). Cách này tránh phải duy trì 2 nguồn sự thật song
// song cho tiếng Việt.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Groups");
    options.Conventions.AuthorizeFolder("/Expenses");
}).AddViewLocalization().AddDataAnnotationsLocalization();
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
var apiBaseUrl = builder.Configuration.GetSection(ApiOptions.SectionName)["BaseUrl"] ?? "http://localhost:5199/api/v1/";

// Client "trần" dùng riêng cho việc refresh token bên trong BearerTokenHandler — không gắn chính
// handler đó vào, tránh đệ quy vô hạn.
builder.Services.AddHttpClient("ApiRaw", client => client.BaseAddress = new Uri(apiBaseUrl));

builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient<SplitBillApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<BearerTokenHandler>();

// ===== Đăng nhập bằng Google (CLAUDE.md mục 25.3, bổ sung 2026-09-09) =====
// "External" là 1 cookie TẠM riêng, chỉ sống trong đúng luồng handshake OAuth (Challenge ->
// Google -> callback đọc claim -> SignOutAsync("External")) — KHÔNG phải cookie đăng nhập chính của
// app (đó vẫn là CookieAuthenticationDefaults.AuthenticationScheme, không đổi). Tách riêng để
// GoogleCallback đọc được claim thô từ Google (sub/email/name) TRƯỚC khi tự quyết định gọi Api và
// build lại principal/token thật qua SignInHelper — không để middleware Google tự ý sign-in thẳng
// vào cookie chính bằng claim chưa qua Api.
var authenticationBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14); // khớp RefreshTokenDays mặc định ở Api
        options.SlidingExpiration = true;
    });

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
// ⚠️ CHỈ đăng ký scheme Google khi đã có ClientId/ClientSecret thật — KHÔNG đăng ký với chuỗi rỗng
// rồi trông chờ handler "im lặng" khi chưa cấu hình. Đã verify bằng chạy thực tế: đăng ký AddGoogle()
// với ClientId rỗng làm SẬP 500 MỌI trang trên Web, không riêng gì trang đăng nhập — vì
// GoogleHandler triển khai IAuthenticationRequestHandler (để tự nhận diện CallbackPath /signin-google),
// nên AuthenticationMiddleware gọi initialize handler đó trên MỌI request để kiểm tra path, và
// RemoteAuthenticationOptions.Validate() ném ArgumentException("ClientId") ngay khi options rỗng —
// hoàn toàn không liên quan gì tới việc người dùng có bấm nút "Đăng nhập bằng Google" hay không. Ẩn
// nút ở LoginModel/RegisterModel (dựa vào đúng 2 biến bên dưới) là không đủ — phải không đăng ký
// scheme luôn thì mới an toàn.
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authenticationBuilder
        .AddCookie("External")
        .AddGoogle(options =>
        {
            options.SignInScheme = "External";
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
        });
}

builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Mặc định "vi" (khớp toàn bộ nội dung .cshtml gốc là tiếng Việt) — người dùng chưa từng chọn ngôn
// ngữ sẽ luôn thấy tiếng Việt, không suy đoán theo Accept-Language trình duyệt (tránh 1 khách nước
// ngoài vô tình thấy giao diện tiếng Anh dù site này chủ yếu phục vụ người dùng Việt Nam). Chỉ đổi khi
// người dùng chủ động bấm nút đổi ngôn ngữ (/SetLanguage — lưu vào cookie
// CookieRequestCultureProvider.DefaultCookieName, đọc lại ở đây).
var supportedCultures = new[] { new CultureInfo("vi"), new CultureInfo("en") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("vi"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    RequestCultureProviders = [new CookieRequestCultureProvider()],
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

namespace SplitBill.Web
{
    /// <summary>Cho phép WebApplicationFactory trong test project (SplitBill.E2ETests) tham chiếu tới
    /// entry point — cùng mẫu đã có sẵn ở SplitBill.Api/Program.cs.</summary>
    public partial class Program;
}
