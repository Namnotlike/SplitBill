using SplitBill.Application.Abstractions;

namespace SplitBill.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SplitBillDbContext _dbContext;

    public UnitOfWork(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
