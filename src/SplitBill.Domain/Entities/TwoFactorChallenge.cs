namespace SplitBill.Domain.Entities;

/// <summary>"Vé tạm" chứng minh đã qua bước email/mật khẩu (hoặc Google) nhưng CHƯA hoàn tất đăng
/// nhập vì tài khoản bật 2FA (CLAUDE.md mục 25.9) — client gửi kèm 1 mã TOTP/mã dự phòng tới
/// <c>POST /auth/login/2fa</c> để đổi lấy AuthTokens thật. Cùng mẫu thiết kế
/// <see cref="PasswordResetToken"/>: chỉ lưu hash (SHA-256), thời hạn NGẮN (mặc định 5 phút, xem
/// <c>JwtOptions.TwoFactorChallengeMinutes</c>), dùng được ĐÚNG 1 LẦN.</summary>
public sealed class TwoFactorChallenge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }

    public bool IsUsable => UsedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
