using Microsoft.Playwright;

namespace SplitBill.E2ETests;

/// <summary>
/// Dựng sẵn 1 cặp <c>SplitBill.Api</c> + <c>SplitBill.Web</c> chạy Kestrel thật trên cổng ngẫu nhiên,
/// cùng 1 trình duyệt Chromium headless (Playwright) — dùng chung cho MỌI test trong cùng 1
/// <c>[Collection]</c> (xem <see cref="E2ECollection"/>) để tránh chi phí khởi động lặp lại cho từng
/// test. Mỗi test tự mở 1 <see cref="IBrowserContext"/> riêng (qua <see cref="NewContextAsync"/>) để
/// cô lập cookie đăng nhập/localStorage giữa các test, dù dùng chung 1 DB InMemory của Api.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    public ApiTestFactory ApiFactory { get; private set; } = null!;
    public WebTestFactory WebFactory { get; private set; } = null!;
    public string WebBaseUrl => WebFactory.ServerAddress;

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        // Tự cài trình duyệt Chromium nếu máy/CI chưa có sẵn (idempotent — bỏ qua rất nhanh nếu đã
        // cài) thay vì bắt buộc 1 bước setup thủ công riêng trước khi `dotnet test` chạy được.
        var installExitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (installExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Không cài được trình duyệt Chromium cho Playwright (exit code {installExitCode}). " +
                "Thử chạy tay: pwsh bin/Debug/net9.0/playwright.ps1 install chromium");
        }

        ApiFactory = new ApiTestFactory();
        using (ApiFactory.CreateClient())
        {
            // Chỉ để ép WebApplicationFactory khởi động host (Kestrel + IServerAddressesFeature) —
            // CreateHost() đã override sẵn để Start() thật, xem KestrelWebApplicationFactory.
        }

        WebFactory = new WebTestFactory(ApiFactory.ServerAddress);
        using (WebFactory.CreateClient())
        {
        }

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public Task<IBrowserContext> NewContextAsync() => _browser.NewContextAsync();

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        // Dừng host Kestrel THẬT trước — xem KestrelWebApplicationFactory.StopRealHostAsync.
        await WebFactory.StopRealHostAsync();
        await ApiFactory.StopRealHostAsync();
        await WebFactory.DisposeAsync();
        await ApiFactory.DisposeAsync();
    }
}

/// <summary>Gom mọi test class E2E vào cùng 1 collection để dùng chung <see cref="E2EFixture"/>
/// (1 lần khởi động Api+Web+trình duyệt cho cả file test) — xUnit đảm bảo các collection khác nhau
/// chạy song song nhưng test TRONG cùng 1 collection chạy tuần tự, tránh 2 test cùng lúc tranh nhau
/// cổng/health-check của cùng 1 cặp server.</summary>
[CollectionDefinition("E2E")]
public sealed class E2ECollection : ICollectionFixture<E2EFixture>;
