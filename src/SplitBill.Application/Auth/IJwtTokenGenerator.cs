using SplitBill.Domain.Entities;

namespace SplitBill.Application.Auth;

/// <summary>Sinh access/refresh token. Cài đặt ở Infrastructure (cần thư viện JWT).</summary>
public interface IJwtTokenGenerator
{
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateAccessToken(User user);

    /// <summary>Sinh refresh token mới: trả cả plaintext (trả về client) và hash (lưu DB).</summary>
    (string PlaintextToken, string Hash, DateTimeOffset ExpiresAt) GenerateRefreshToken();

    /// <summary>Băm refresh token plaintext (SHA-256) để tra cứu trong DB.</summary>
    string HashRefreshToken(string plaintextToken);
}
