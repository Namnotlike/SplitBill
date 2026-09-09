using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface ITwoFactorChallengeRepository
{
    Task AddAsync(TwoFactorChallenge challenge, CancellationToken cancellationToken);

    Task<TwoFactorChallenge?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);
}
