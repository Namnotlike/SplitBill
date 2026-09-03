namespace SplitBill.Domain.Entities;

/// <summary>Người dùng đã đăng ký tài khoản. Khách vãng lai KHÔNG có record User (xem GroupMember).</summary>
public sealed class User
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? BankAccountNumber { get; set; }
    public string? BankBin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
