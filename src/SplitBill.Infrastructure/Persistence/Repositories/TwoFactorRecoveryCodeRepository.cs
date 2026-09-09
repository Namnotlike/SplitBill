using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class TwoFactorRecoveryCodeRepository : ITwoFactorRecoveryCodeRepository
{
    private readonly SplitBillDbContext _dbContext;

    public TwoFactorRecoveryCodeRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(TwoFactorRecoveryCode code, CancellationToken cancellationToken) =>
        await _dbContext.TwoFactorRecoveryCodes.AddAsync(code, cancellationToken);

    public async Task AddRangeAsync(IEnumerable<TwoFactorRecoveryCode> codes, CancellationToken cancellationToken) =>
        await _dbContext.TwoFactorRecoveryCodes.AddRangeAsync(codes, cancellationToken);

    public Task<TwoFactorRecoveryCode?> GetByHashAsync(string codeHash, CancellationToken cancellationToken) =>
        _dbContext.TwoFactorRecoveryCodes.FirstOrDefaultAsync(c => c.CodeHash == codeHash, cancellationToken);

    public Task<List<TwoFactorRecoveryCode>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.TwoFactorRecoveryCodes.Where(c => c.UserId == userId).ToListAsync(cancellationToken);

    public async Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.TwoFactorRecoveryCodes.Where(c => c.UserId == userId).ToListAsync(cancellationToken);
        _dbContext.TwoFactorRecoveryCodes.RemoveRange(existing);
    }
}
