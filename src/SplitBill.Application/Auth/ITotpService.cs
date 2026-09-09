namespace SplitBill.Application.Auth;

/// <summary>Thuật toán TOTP thuần (RFC 6238), không phụ thuộc DB — cùng tinh thần "thuật toán thuần,
/// test dễ dàng" đã áp dụng cho settlement (CLAUDE.md mục 3/6). CLAUDE.md mục 25.9.</summary>
public interface ITotpService
{
    /// <summary>Sinh secret ngẫu nhiên 160-bit (khuyến nghị RFC 4226 §4), trả về dạng Base32 (chuẩn
    /// hiển thị/nhập tay cho app xác thực).</summary>
    string GenerateSecret();

    /// <summary>Dựng URI <c>otpauth://totp/...</c> để vẽ QR — app xác thực (Google/Microsoft
    /// Authenticator, Authy...) quét là thêm được tài khoản ngay.</summary>
    string BuildOtpAuthUri(string secretBase32, string accountLabel);

    /// <summary>So khớp mã 6 chữ số với secret trong cửa sổ ±<paramref name="windowSteps"/> bước (mỗi
    /// bước 30s, bù trừ lệch giờ giữa client/server) — trả về time step ĐÃ KHỚP (dùng để chống replay,
    /// xem <c>User.TwoFactorLastUsedTimeStep</c>) hoặc null nếu không mã nào trong cửa sổ khớp.</summary>
    long? TryMatchTimeStep(string secretBase32, string code, DateTimeOffset now, int windowSteps = 1);
}
