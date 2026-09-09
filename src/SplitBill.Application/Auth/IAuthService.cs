namespace SplitBill.Application.Auth;

public interface IAuthService
{
    Task<AuthTokens> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    /// <summary>Kiểm tra email/mật khẩu — nếu tài khoản bật 2FA (CLAUDE.md mục 25.9), trả về
    /// <see cref="LoginResult.RequiresTwoFactor"/> = true kèm challenge token thay vì AuthTokens thật
    /// ngay, gọi tiếp <see cref="CompleteTwoFactorLoginAsync"/> để hoàn tất.</summary>
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Luôn "thành công" âm thầm (không ném lỗi, không tiết lộ email có tồn tại hay không —
    /// CLAUDE.md mục 8) dù email có khớp tài khoản nào hay không. Nếu khớp, gửi email chứa link đặt
    /// lại mật khẩu.</summary>
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken);

    /// <summary>Đổi mật khẩu bằng token nhận qua email. Token chỉ dùng được 1 lần, hết hạn sau
    /// <see cref="JwtOptions.PasswordResetTokenMinutes"/> phút. Thành công thì thu hồi TOÀN BỘ
    /// RefreshToken hiện có của user (đổi mật khẩu = đăng xuất mọi phiên khác, phòng trường hợp mật
    /// khẩu cũ đã bị lộ).</summary>
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);

    /// <summary>Đăng nhập/đăng ký qua Google (CLAUDE.md mục 25.3). Tìm theo GoogleId trước; nếu chưa
    /// liên kết nhưng Email đã có tài khoản (đăng ký bằng mật khẩu từ trước) thì tự liên kết (Google
    /// đã xác thực chủ sở hữu email); nếu chưa từng tồn tại thì tạo User mới với PasswordHash null
    /// (chỉ đăng nhập được qua Google, không có mật khẩu). CÙNG đi qua cổng 2FA như LoginAsync (mục
    /// 25.9) — đăng nhập qua Google KHÔNG được phép bỏ qua 2FA của tài khoản, nếu không 2FA sẽ không
    /// còn ý nghĩa với người dùng đã liên kết cả 2 cách đăng nhập.</summary>
    Task<LoginResult> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken);

    /// <summary>Hoàn tất đăng nhập sau khi LoginAsync/GoogleLoginAsync trả về RequiresTwoFactor = true
    /// (CLAUDE.md mục 25.9) — xác minh challenge token + mã TOTP/mã dự phòng, phát AuthTokens thật.</summary>
    Task<AuthTokens> CompleteTwoFactorLoginAsync(CompleteTwoFactorLoginRequest request, CancellationToken cancellationToken);
}
