using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SplitBill.Application.Auth;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Security;

/// <summary>Cài đặt JWT theo cấu hình chốt ở CLAUDE.md mục 2.</summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string AccessToken, DateTimeOffset ExpiresAt) GenerateAccessToken(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (user.Email is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string PlaintextToken, string Hash, DateTimeOffset ExpiresAt) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Base64UrlEncoder.Encode(bytes);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays);

        return (plaintext, HashRefreshToken(plaintext), expiresAt);
    }

    public string HashRefreshToken(string plaintextToken)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextToken));
        return Convert.ToBase64String(hashBytes);
    }

    public (string PlaintextToken, string Hash, DateTimeOffset ExpiresAt) GeneratePasswordResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Base64UrlEncoder.Encode(bytes);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.PasswordResetTokenMinutes);

        return (plaintext, HashRefreshToken(plaintext), expiresAt);
    }
}
