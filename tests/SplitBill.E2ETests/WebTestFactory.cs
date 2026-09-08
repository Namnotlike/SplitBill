using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace SplitBill.E2ETests;

/// <summary>
/// Host thật (Kestrel, cổng ngẫu nhiên) của <c>SplitBill.Web</c> cho E2E test, trỏ
/// <c>Api:BaseUrl</c> về đúng địa chỉ động của <see cref="ApiTestFactory"/> đang chạy song song.
/// </summary>
public sealed class WebTestFactory : KestrelWebApplicationFactory<SplitBill.Web.Program>
{
    private readonly string _apiBaseUrl;

    public WebTestFactory(string apiServerAddress)
    {
        _apiBaseUrl = apiServerAddress.TrimEnd('/') + "/api/v1/";

        // ⚠️ KHÔNG dùng ConfigureAppConfiguration ở đây — đã thử và xác nhận KHÔNG có tác dụng: Program.cs
        // đọc "Api:BaseUrl" bằng builder.Configuration[...] NGAY SAU WebApplication.CreateBuilder(args)
        // rồi đóng gói kết quả vào 1 biến local (apiBaseUrl) truyền cho AddHttpClient — đọc 1 LẦN DUY
        // NHẤT, sớm hơn nhiều so với lúc callback ConfigureAppConfiguration của WebApplicationFactory
        // thực sự chạy (chỉ chạy trong lúc IHostBuilder.Build(), sau khi Program.cs đã đọc xong). Verify
        // trực tiếp: dùng ConfigureAppConfiguration thì request thật vẫn bay thẳng tới cổng 5199 mặc định
        // trong appsettings.json, không phải cổng ngẫu nhiên của ApiTestFactory.
        //
        // Cách đúng: đặt biến môi trường TRƯỚC KHI Program.cs chạy (tức trước khi EnsureRealHostStarted
        // trong KestrelWebApplicationFactory kích hoạt host) — WebApplication.CreateBuilder(args) tự thêm
        // AddEnvironmentVariables() làm 1 trong các nguồn config ĐẦU TIÊN, đọc đồng bộ ngay lúc đó, nên
        // kịp áp dụng trước dòng builder.Configuration["Api:BaseUrl"] của Program.cs. Cùng quy ước
        // "__" -> ":" đã dùng cho docker-compose (CLAUDE.md mục 10b, biến Database__AutoMigrateOnStartup).
        Environment.SetEnvironmentVariable("Api__BaseUrl", _apiBaseUrl);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // BẮT BUỘC "Development" — CLAUDE.md mục 10b: MapStaticAssets() ở môi trường Production trông
        // đợi asset đã nén sẵn từ bước `dotnet publish`; chạy qua `dotnet build`/`dotnet test` (không
        // publish) mà môi trường lại là Production khiến mọi CSS/JS trả về 200 nhưng body RỖNG.
        builder.UseEnvironment("Development");
    }
}
