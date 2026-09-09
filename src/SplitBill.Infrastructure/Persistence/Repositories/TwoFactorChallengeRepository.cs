using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class TwoFactorChallengeRepository : ITwoFactorChallengeRepository
{
    private readonly SplitBillDbContext _dbContext;

    public TwoFactorChallengeRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(TwoFactorChallenge challenge, CancellationToken cancellationToken) =>
        await _dbContext.TwoFactorChallenges.AddAsync(challenge, cancellationToken);

    public Task<TwoFactorChallenge?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _dbContext.TwoFactorChallenges.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
}
