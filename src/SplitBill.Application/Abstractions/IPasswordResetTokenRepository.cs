using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IPasswordResetTokenRepository
{
    Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken);

    /// <summary>Tìm theo hash (SHA-256 của token plaintext) — không bao giờ tìm theo plaintext.</summary>
    Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);
}
