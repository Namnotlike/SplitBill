using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>Tìm theo hash (SHA-256 của token plaintext) — không bao giờ tìm theo plaintext.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);
}
