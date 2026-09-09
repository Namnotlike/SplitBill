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
