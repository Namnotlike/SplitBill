using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using SplitBill.Api.Middleware;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Auth;
using SplitBill.Application.Expenses;
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
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "SplitBill.Api dừng đột ngột khi khởi động");
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
