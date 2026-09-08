using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SplitBill.Infrastructure.Persistence;

namespace SplitBill.E2ETests;

/// <summary>
/// Host thật (Kestrel, cổng ngẫu nhiên) của <c>SplitBill.Api</c> cho E2E test — thay <c>UseSqlServer</c>
/// (LocalDB, chỉ chạy được trên Windows — CLAUDE.md mục 2) bằng EF Core InMemory, CÙNG PATTERN đã dùng
/// ở <c>TestHarness</c> (SplitBill.IntegrationTests) để bộ test này chạy được cả trên CI (ubuntu-latest,
/// không có LocalDB). Mỗi factory một DB InMemory riêng, đặt tên theo <see cref="Guid.NewGuid"/> — cô
/// lập hoàn toàn với các lần chạy test khác trên cùng máy.
/// </summary>
public sealed class ApiTestFactory : KestrelWebApplicationFactory<SplitBill.Api.Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public ApiTestFactory()
    {
        // ⚠️ KHÔNG dùng builder.ConfigureAppConfiguration để cấp Jwt:SigningKey giả — đã thử và phát
        // hiện KHÔNG có tác dụng đáng tin cậy: Program.cs của Api đọc jwtOptions.SigningKey (rồi
        // fail-fast nếu rỗng/ngắn) NGAY SAU WebApplication.CreateBuilder(args), sớm hơn nhiều so với
        // lúc callback ConfigureAppConfiguration của WebApplicationFactory thực sự chạy (chỉ chạy
        // trong lúc IHostBuilder.Build(), sau khi Program.cs đã đọc xong) — cùng lớp lỗi timing đã tìm
        // ra ở WebTestFactory (xem comment ở đó). Lần chạy đầu tiên "tình cờ" không lộ ra lỗi này vì
        // máy dev cục bộ đã có sẵn `dotnet user-secrets set Jwt:SigningKey ...` (CLAUDE.md mục 10b) —
        // giá trị đó mới thực sự được dùng, không phải giá trị test cấp qua ConfigureAppConfiguration.
        // Trên CI (không có user-secrets) chắc chắn sẽ fail-fast ngay khi khởi động.
        //
        // Cách đúng: đặt biến môi trường TRƯỚC KHI Program.cs chạy, cùng quy ước "__" -> ":" đã dùng
        // cho docker-compose (CLAUDE.md mục 10b).
        //
        // ⚠️ Environment.SetEnvironmentVariable là state TOÀN TIẾN TRÌNH, không riêng cho factory này —
        // an toàn CHỈ VÌ mọi test class dùng ApiTestFactory/WebTestFactory đều nằm chung
        // [Collection("E2E")] (E2EFixture.cs), đảm bảo chạy TUẦN TỰ, không có 2 factory nào cùng lúc
        // đặt lại biến này. Nếu sau này thêm 1 test class E2E MỚI mà quên gắn collection này, xUnit có
        // thể chạy nó song song với collection khác — 2 factory ghi đè biến môi trường của nhau, gây
        // flaky khó chẩn đoán (không phải lỗi biên dịch). Luôn gắn [Collection("E2E")] cho MỌI test
        // class dùng tới ApiTestFactory/WebTestFactory.
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "e2e-test-signing-key-at-least-32-characters-long!");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseEnvironment("Development");

        // Đăng ký/gỡ DbContextOptions qua ConfigureServices KHÔNG bị lỗi timing như trên — đây là mutate
        // trực tiếp IServiceCollection (áp dụng lúc Build()), khác với đọc 1 local variable đã đóng gói
        // từ trước, nên override DB sang InMemory vẫn hoạt động đúng dù đăng ký muộn.
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<SplitBillDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            // ⚠️ Gỡ DbContextOptions<SplitBillDbContext> KHÔNG đủ — Program.cs gốc gọi UseSqlServer(...)
            // đã có tác dụng phụ đăng ký sẵn dịch vụ nội bộ của provider SqlServer (AddEntityFrameworkSqlServer)
            // thẳng vào IServiceCollection dùng chung của cả app, việc gỡ 1 descriptor phía trên không undo
            // được tác dụng phụ đó. Nếu chỉ AddDbContext(UseInMemoryDatabase(...)) như bình thường, EF Core
            // phát hiện CẢ HAI provider (SqlServer + InMemory) cùng có dịch vụ trong 1 service provider gốc
            // và ném InvalidOperationException "Only a single database provider can be registered" — xác
            // nhận qua chạy thực tế. Cách đúng (mẫu chính thức của Microsoft cho đúng tình huống swap
            // provider này): dựng 1 service provider RIÊNG chỉ chứa dịch vụ InMemory rồi UseInternalServiceProvider
            // trỏ DbContextOptions mới về đúng service provider cô lập đó, không đụng tới service provider
            // gốc (vẫn còn SqlServer) của app.
            var inMemoryServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<SplitBillDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.UseInternalServiceProvider(inMemoryServiceProvider);
            });
        });
    }
}
