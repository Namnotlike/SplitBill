namespace SplitBill.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

// ===== Quên mật khẩu (CLAUDE.md mục 16, bổ sung 2026-09-07) =====
public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

// ===== Đăng nhập bằng Google (CLAUDE.md mục 25.3, bổ sung 2026-09-09) =====
/// <summary>Chỉ gọi được từ SplitBill.Web (đã tự xác thực claim với Google) kèm header
/// X-Internal-Secret đúng — xem GoogleAuthOptions.</summary>
public sealed record GoogleLoginRequest(string GoogleId, string Email, string DisplayName);

// ===== Xác thực 2 lớp / TOTP (CLAUDE.md mục 25.9, bổ sung 2026-09-09) =====
/// <summary>Kết quả của LoginAsync/GoogleLoginAsync — HOẶC đăng nhập xong ngay (<see cref="Tokens"/>
/// khác null), HOẶC tài khoản bật 2FA nên cần thêm 1 bước (<see cref="TwoFactorChallengeToken"/> khác
/// null, gửi kèm mã 2FA tới POST /auth/login/2fa để hoàn tất). Đúng 1 trong 2 khác null, không bao giờ
/// cả hai hoặc không cái nào.</summary>
public sealed record LoginResult(bool RequiresTwoFactor, AuthTokens? Tokens, string? TwoFactorChallengeToken);

public sealed record CompleteTwoFactorLoginRequest(string ChallengeToken, string Code);
