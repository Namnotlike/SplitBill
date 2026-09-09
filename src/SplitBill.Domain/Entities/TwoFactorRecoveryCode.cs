namespace SplitBill.Domain.Entities;

/// <summary>Mã dự phòng đăng nhập 2FA (CLAUDE.md mục 25.9) — sinh 1 lần lúc bật 2FA (10 mã), mỗi mã
/// dùng được ĐÚNG 1 LẦN, để phòng trường hợp mất điện thoại/không truy cập được app xác thực. Chỉ lưu
/// hash (SHA-256), không lưu plaintext — cùng mẫu <see cref="RefreshToken"/>/<see cref="PasswordResetToken"/>.</summary>
public sealed class TwoFactorRecoveryCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }

    public bool IsUsable => UsedAt is null;
}
