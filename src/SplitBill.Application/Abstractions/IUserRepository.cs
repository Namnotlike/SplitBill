using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Đăng nhập bằng Google (CLAUDE.md mục 25.3).</summary>
    Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken cancellationToken);
    Task AddAsync(User user, CancellationToken cancellationToken);
}
