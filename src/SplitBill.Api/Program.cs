using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using SplitBill.Api.Middleware;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Auth;
using SplitBill.Application.Expenses;
using SplitBill.Application.Export;
using SplitBill.Application.Groups;
using SplitBill.Application.Settlement;
using SplitBill.Application.Settlements;
using SplitBill.Application.Splitting;
using SplitBill.Application.Users;
using SplitBill.Application.VietQr;
using SplitBill.Infrastructure.Persistence;
using SplitBill.Infrastructure.Persistence.Repositories;
using SplitBill.Infrastructure.Security;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddDbContext<SplitBillDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

    // ===== JWT =====
    builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

    // Fail-fast: không cho chạy với SigningKey rỗng/yếu — tránh lặp lại lỗi từng gặp lúc dev
    // (đã có lần lỡ set toàn số 0 do RandomNumberGenerator.Fill không tồn tại trên PowerShell 5.1
    // .NET Framework — nay bắt buộc kiểm tra độ dài & không rỗng ngay khi khởi động).
    if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey chưa được cấu hình hoặc quá ngắn (cần >= 32 byte). " +
            "Dev: chạy `dotnet user-secrets set \"Jwt:SigningKey\" \"<chuỗi random dài>\"` trong " +
            "src/SplitBill.Api. Production: đặt qua biến môi trường Jwt__SigningKey hoặc Key Vault, " +
            "KHÔNG bao giờ commit key thật vào appsettings.json.");
    }

    // ===== CORS =====
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddPolicy("Default", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    }));

    // ===== Rate limiting cho /auth (chống brute-force) =====
    // ⚠️ Sửa lỗi phát hiện khi rà soát 2026-09-04: AddFixedWindowLimiter (không có partition key)
    // tạo ĐÚNG 1 hạn ngạch dùng chung cho MỌI client, không phải "10 request/phút/IP" như tài liệu
    // ban đầu mô tả. Hậu quả: vài user đăng nhập cùng lúc có thể vô tình khóa đăng nhập của TẤT CẢ
    // user khác trong 1 phút (tự gây DoS), và không hề chống được brute-force phân tán nhiều IP.
    // Phải dùng AddPolicy + RateLimitPartition.GetFixedWindowLimiter với partition key = IP để mỗi
    // client có hạn ngạch riêng.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    });

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false; // giữ nguyên tên claim "sub", không map sang ClaimTypes.*
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
    builder.Services.AddAuthorization();

    // ===== Repositories / Infrastructure =====
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
    builder.Services.AddScoped<IGroupRepository, GroupRepository>();
    builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
    builder.Services.AddScoped<ISettlementRepository, SettlementRepository>();
    builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
    builder.Services.AddScoped<IReceiptImageRepository, ReceiptImageRepository>();
    builder.Services.AddSingleton<IShareTokenGenerator, ShareTokenGenerator>();
    builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

    // ===== Application services (thuật toán thuần — có thể singleton) =====
    builder.Services.AddSingleton<IExpenseSplitCalculator, ExpenseSplitCalculator>();
    builder.Services.AddSingleton<IBalanceCalculator, BalanceCalculator>();
    builder.Services.AddSingleton<IVietQrGenerator, VietQrGenerator>();

    // ===== Application services (nghiệp vụ) =====
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IGroupService, GroupService>();
    builder.Services.AddScoped<IExpenseService, ExpenseService>();
    builder.Services.AddScoped<IBalanceService, BalanceService>();
    builder.Services.AddScoped<ISettlementRecordService, SettlementRecordService>();
    builder.Services.AddScoped<IExportService, ExportService>();

    // ===== FluentValidation =====
    builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

    var app = builder.Build();

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseCors("Default");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "SplitBill.Api dừng đột ngột khi khởi động");
    // ⚠️ Sửa lỗi phát hiện khi rà soát 2026-09-04: thiếu dòng này khiến process thoát với exit code 0
    // dù thực chất là crash lúc khởi động (từng thấy trực tiếp: "[exited with code 0]" khi thiếu
    // Jwt:SigningKey). Exit code 0 = "tắt bình thường" đối với Docker/K8s/systemd — orchestrator sẽ
    // KHÔNG tự restart hay báo lỗi, rất nguy hiểm nếu deploy production thiếu biến môi trường bắt
    // buộc. Dùng Environment.ExitCode (không dùng Environment.Exit) để vẫn chạy xong khối finally
    // bên dưới (flush log) trước khi process thật sự thoát với exit code khác 0.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

namespace SplitBill.Api
{
    /// <summary>Cho phép WebApplicationFactory trong test project tham chiếu tới entry point.</summary>
    public partial class Program;
}
