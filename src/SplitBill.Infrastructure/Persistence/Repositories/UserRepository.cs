using Microsoft.EntityFrameworkCore;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;

namespace SplitBill.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly SplitBillDbContext _dbContext;

    public UserRepository(SplitBillDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken) =>
        _dbContext.Users.FirstOrDefaultAsync(u => u.GoogleId == googleId, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken) =>
        await _dbContext.Users.AddAsync(user, cancellationToken);
}
