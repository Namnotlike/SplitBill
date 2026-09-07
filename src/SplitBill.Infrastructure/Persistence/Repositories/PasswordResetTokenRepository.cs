using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly SplitBillDbContext _dbContext;

    public PasswordResetTokenRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken) =>
        await _dbContext.PasswordResetTokens.AddAsync(token, cancellationToken);

    public Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _dbContext.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
}
