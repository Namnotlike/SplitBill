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

    /// <summary>Token đặt lại mật khẩu (CLAUDE.md mục 16, bổ sung 2026-09-07) — ngắn hơn RefreshToken
    /// nhiều vì đây là "cửa sổ" gửi qua email công khai, chỉ cần đủ thời gian người dùng mở email
    /// và bấm link, không cần sống lâu như phiên đăng nhập.</summary>
    public int PasswordResetTokenMinutes { get; set; } = 30;
}
