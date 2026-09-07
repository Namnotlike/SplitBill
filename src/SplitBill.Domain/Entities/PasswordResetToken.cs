namespace SplitBill.Domain.Entities;

/// <summary>Token đặt lại mật khẩu (CLAUDE.md mục 16 — Quên mật khẩu, bổ sung 2026-09-07). Cùng mẫu
/// thiết kế với <see cref="RefreshToken"/>: chỉ lưu hash (SHA-256), không lưu plaintext; token thật
/// chỉ tồn tại trong link gửi qua email, không bao giờ chạm tới DB ở dạng đọc được.</summary>
public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Đã được dùng để đổi mật khẩu chưa — token chỉ dùng được ĐÚNG 1 LẦN, khác RefreshToken
    /// (có thể refresh nhiều lần trước khi hết hạn). Dùng rồi thì dù còn hạn cũng không dùng lại được.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }

    public bool IsUsable => UsedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
