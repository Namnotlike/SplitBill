using SplitBill.Domain.Entities;

namespace SplitBill.Application.Abstractions;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>Tìm theo hash (SHA-256 của token plaintext) — không bao giờ tìm theo plaintext.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Thu hồi (đặt RevokedAt) mọi RefreshToken còn hiệu lực của 1 user — dùng khi đặt lại
    /// mật khẩu (CLAUDE.md mục 16): đổi mật khẩu nên đăng xuất mọi phiên khác, phòng mật khẩu cũ đã bị
    /// lộ. Không xóa cứng (giữ lịch sử), chỉ đánh dấu thu hồi giống rotation bình thường.</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken);
}
