namespace SplitBill.Application.Common;

/// <summary>Cấu hình xác thực 2 lớp / TOTP (CLAUDE.md mục 25.9). Không fail-fast nếu thiếu — cùng
/// nguyên tắc `GoogleAuthOptions`/`WebPushOptions`: tính năng tùy chọn, để trống chỉ khiến
/// `POST /users/me/2fa/setup` từ chối (fail closed, TWO_FACTOR_NOT_CONFIGURED), không chặn Api chạy.</summary>
public sealed class TwoFactorOptions
{
    public const string SectionName = "TwoFactor";

    /// <summary>Khóa mã hóa secret TOTP tại rest (AES-GCM, xem `TwoFactorSecretProtector`) — chuỗi
    /// bất kỳ ≥ 32 ký tự, cùng cách quản lý như `Jwt:SigningKey` (user-secrets/biến môi trường, KHÔNG
    /// đặt giá trị thật vào appsettings.json).</summary>
    public string EncryptionKey { get; set; } = string.Empty;
}
