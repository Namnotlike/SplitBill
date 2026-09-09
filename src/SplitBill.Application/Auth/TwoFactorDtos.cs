namespace SplitBill.Application.Auth;

/// <summary>Xác thực 2 lớp / TOTP (CLAUDE.md mục 25.9).</summary>
public sealed record TwoFactorStatusDto(bool Enabled);

/// <summary>Trả về lúc gọi <c>/2fa/setup</c> — <see cref="SecretBase32"/> để người dùng nhập tay nếu
/// không quét được QR, <see cref="OtpAuthUri"/> để vẽ QR (client-side, cùng thư viện qrcodejs đã dùng
/// cho VietQR mục 9). 2FA CHƯA bật ngay — phải xác nhận bằng 1 mã hợp lệ qua <c>/2fa/enable</c>.</summary>
public sealed record TwoFactorSetupDto(string SecretBase32, string OtpAuthUri);

public sealed record EnableTwoFactorRequest(string Code);

/// <summary>10 mã dự phòng dạng PLAINTEXT — CHỈ trả về ĐÚNG 1 LẦN tại thời điểm bật/sinh lại (DB chỉ
/// lưu hash, không bao giờ đọc lại được plaintext sau lần này).</summary>
public sealed record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

public sealed record DisableTwoFactorRequest(string Code);

public sealed record RegenerateRecoveryCodesRequest(string Code);
