using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SplitBill.E2ETests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> mặc định host ứng dụng qua <c>TestServer</c> —
/// gọi được bằng <c>HttpClient</c> in-process nhưng KHÔNG lắng nghe cổng mạng thật, nên trình duyệt
/// Playwright (một tiến trình Chromium thật, tách biệt) không thể kết nối tới.
///
/// ⚠️ .NET 9 CHƯA có <c>UseKestrel()</c> built-in cho <c>WebApplicationFactory</c> (tính năng này chỉ
/// có từ .NET 10 — đã xác nhận qua tìm hiểu, không suy đoán). Chỉ đơn giản override
/// <see cref="CreateHost"/> để build 1 host Kestrel duy nhất rồi trả thẳng về sẽ ném
/// <c>InvalidCastException: Unable to cast ... KestrelServerImpl ... to ... TestServer</c> — vì
/// <c>EnsureServer()</c> (gọi ngầm bởi <c>CreateClient()</c>/<c>Server</c>/<c>Services</c>) LUÔN ép kiểu
/// host trả về sang <c>TestServer</c>, bất kể <c>CreateHost</c> đã override gì.
///
/// Cách né đúng (theo mẫu cộng đồng đã kiểm chứng — xem
/// https://danieldonbavand.com/2022/06/13/using-playwright-with-the-webapplicationfactory-to-test-a-blazor-application/):
/// build CẢ HAI host từ CÙNG 1 <see cref="IHostBuilder"/> — build lần 1 (chưa gắn Kestrel) để có 1
/// "vỏ" <c>TestServer</c> thỏa mãn yêu cầu ép kiểu nội bộ của <c>WebApplicationFactory</c> (không bao
/// giờ dùng để gọi HTTP thật), rồi mới `ConfigureWebHost(UseKestrel)` và build lần 2 để có host Kestrel
/// THẬT — khởi động, đọc lại địa chỉ cổng ngẫu nhiên đã bind, đây mới là host phục vụ request thật từ
/// Playwright.
/// </summary>
public class KestrelWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    private IHost? _realHost;

    /// <summary>Địa chỉ gốc thật (vd "http://127.0.0.1:53214") sau khi Kestrel đã khởi động.</summary>
    public string ServerAddress
    {
        get
        {
            EnsureRealHostStarted();
            return ClientOptions.BaseAddress.ToString().TrimEnd('/');
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Build lần 1: "vỏ" TestServer — WebApplicationFactory.EnsureServer() sẽ ép kiểu giá trị trả
        // về của CreateHost() sang TestServer; host này tồn tại CHỈ để lần ép kiểu đó không ném lỗi,
        // không phục vụ request nào cả.
        var testHost = builder.Build();

        // Build lần 2 TRÊN CÙNG builder, sau khi thêm UseKestrel — IHostBuilder.Build() gọi được nhiều
        // lần, mỗi lần chạy lại toàn bộ action đã đăng ký (cộng dồn); lần này có thêm UseKestrel nên ra
        // 1 host Kestrel thật, độc lập với testHost ở trên.
        builder.ConfigureWebHost(webHostBuilder => webHostBuilder.UseKestrel().UseUrls("http://127.0.0.1:0"));
        _realHost = builder.Build();
        _realHost.Start();

        var server = _realHost.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()!;
        ClientOptions.BaseAddress = new Uri(addresses.Addresses.First());

        testHost.Start();
        return testHost;
    }

    private void EnsureRealHostStarted()
    {
        if (_realHost is null)
        {
            // CreateHost() chỉ chạy khi WebApplicationFactory thật sự cần dựng host lần đầu — kích
            // hoạt bằng 1 lệnh gọi CreateClient() rồi bỏ luôn (client này gọi vào TestServer "vỏ",
            // không dùng tới), đúng như CreateHost() đã cấu hình ClientOptions.BaseAddress sẵn ở trên.
            using var _ = CreateClient();
        }
    }

    /// <summary>
    /// Dừng host Kestrel THẬT (<see cref="_realHost"/>) — <c>WebApplicationFactory.DisposeAsync()</c>
    /// (gọi bởi lớp cơ sở) chỉ biết dọn "vỏ" TestServer trả về từ <see cref="CreateHost"/>, không biết
    /// gì về host thứ 2 này. Gọi tường minh method này TRƯỚC khi gọi <c>DisposeAsync()</c> của chính
    /// factory (xem <see cref="E2EFixture.DisposeAsync"/>) — không override <c>Dispose(bool)</c>/
    /// <c>DisposeAsync()</c> của lớp cơ sở vì không có hook async an toàn nào được expose công khai để
    /// làm điều đó đúng cách (tránh <c>async void</c>).
    /// </summary>
    public async Task StopRealHostAsync()
    {
        if (_realHost is not null)
        {
            await _realHost.StopAsync();
            _realHost.Dispose();
        }
    }
}
