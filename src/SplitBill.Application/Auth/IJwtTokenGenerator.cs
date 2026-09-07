using SplitBill.Domain.Entities;

namespace SplitBill.Application.Auth;

/// <summary>Sinh access/refresh token. Cài đặt ở Infrastructure (cần thư viện JWT).</summary>
public interface IJwtTokenGenerator
{
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateAccessToken(User user);

    /// <summary>Sinh refresh token mới: trả cả plaintext (trả về client) và hash (lưu DB).</summary>
    (string PlaintextToken, string Hash, DateTimeOffset ExpiresAt) GenerateRefreshToken();

    /// <summary>Băm refresh token plaintext (SHA-256) để tra cứu trong DB. Hàm băm chung — cũng dùng
    /// lại cho <see cref="GeneratePasswordResetToken"/> (cùng thuật toán, không cần hàm riêng).</summary>
    string HashRefreshToken(string plaintextToken);

    /// <summary>Sinh token đặt lại mật khẩu (CLAUDE.md mục 16) — cùng cơ chế random 256-bit +
    /// SHA-256 hash như RefreshToken, chỉ khác thời hạn ngắn hơn (<see cref="JwtOptions.PasswordResetTokenMinutes"/>).</summary>
    (string PlaintextToken, string Hash, DateTimeOffset ExpiresAt) GeneratePasswordResetToken();
}
