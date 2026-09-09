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

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
}
