namespace SplitBill.Application.Auth;

/// <summary>Cấu hình JWT (CLAUDE.md mục 2). Bind từ appsettings.json ở tầng Api.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}
