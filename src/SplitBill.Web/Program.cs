using Microsoft.AspNetCore.Authentication.Cookies;
using SplitBill.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Groups");
    options.Conventions.AuthorizeFolder("/Expenses");
});
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
var apiBaseUrl = builder.Configuration.GetSection(ApiOptions.SectionName)["BaseUrl"] ?? "http://localhost:5199/api/v1/";

// Client "trần" dùng riêng cho việc refresh token bên trong BearerTokenHandler — không gắn chính
// handler đó vào, tránh đệ quy vô hạn.
builder.Services.AddHttpClient("ApiRaw", client => client.BaseAddress = new Uri(apiBaseUrl));

builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient<SplitBillApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14); // khớp RefreshTokenDays mặc định ở Api
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
