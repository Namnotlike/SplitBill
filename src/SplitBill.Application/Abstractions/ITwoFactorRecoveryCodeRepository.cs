using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface ITwoFactorRecoveryCodeRepository
{
    Task AddAsync(TwoFactorRecoveryCode code, CancellationToken cancellationToken);

    Task AddRangeAsync(IEnumerable<TwoFactorRecoveryCode> codes, CancellationToken cancellationToken);

    Task<TwoFactorRecoveryCode?> GetByHashAsync(string codeHash, CancellationToken cancellationToken);

    Task<List<TwoFactorRecoveryCode>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Xóa toàn bộ mã dự phòng cũ của user — dùng khi tắt 2FA hoặc sinh lại bộ mã mới.</summary>
    Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken);
}
