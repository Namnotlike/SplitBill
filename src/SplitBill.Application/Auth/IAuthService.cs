namespace SplitBill.Application.Auth;

public interface IAuthService
{
    Task<AuthTokens> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
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
    /// (chỉ đăng nhập được qua Google, không có mật khẩu).</summary>
    Task<AuthTokens> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken);
}
