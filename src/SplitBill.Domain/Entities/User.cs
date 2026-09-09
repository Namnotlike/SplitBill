namespace SplitBill.Domain.Entities;

/// <summary>Người dùng đã đăng ký tài khoản. Khách vãng lai KHÔNG có record User (xem GroupMember).</summary>
public sealed class User
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    /// <summary>"sub" claim của tài khoản Google đã liên kết — null nếu chưa từng đăng nhập bằng
    /// Google. Tài khoản chỉ tạo qua Google (chưa từng đặt mật khẩu) có <see cref="PasswordHash"/>
    /// null (CLAUDE.md mục 25.3).</summary>
    public string? GoogleId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? BankAccountNumber { get; set; }
    public string? BankBin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // ===== Xác thực 2 lớp / TOTP (CLAUDE.md mục 25.9, bổ sung 2026-09-09) =====
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Secret TOTP (Base32) đã mã hóa bằng <c>ITwoFactorSecretProtector</c> (AES-GCM, khóa
    /// riêng — CLAUDE.md mục 25.9) — null nếu chưa từng thiết lập. Vẫn có thể khác null dù
    /// <see cref="TwoFactorEnabled"/> = false (trạng thái "đang thiết lập, chưa xác nhận" — gọi lại
    /// /2fa/setup sẽ ghi đè bằng secret mới).</summary>
    public string? TwoFactorSecretEncrypted { get; set; }

    /// <summary>Time step (Unix time / 30s) của mã TOTP gần nhất đã dùng thành công — chống replay
    /// (dùng lại đúng 1 mã trong cùng cửa sổ ±1 bước trước khi nó tự hết hạn).</summary>
    public long? TwoFactorLastUsedTimeStep { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public ICollection<TwoFactorRecoveryCode> TwoFactorRecoveryCodes { get; set; } = new List<TwoFactorRecoveryCode>();
}
