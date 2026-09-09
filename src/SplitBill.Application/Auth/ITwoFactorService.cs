using SplitBill.Domain.Entities;

namespace SplitBill.Application.Auth;

/// <summary>CLAUDE.md mục 25.9 — quản lý bật/tắt 2FA + mã dự phòng cho user hiện tại. Việc XÁC MINH mã
/// lúc hoàn tất đăng nhập (challenge flow) cũng đi qua đây (<see cref="VerifyCodeOrRecoveryAsync"/>) để
/// dùng chung đúng 1 chỗ logic "TOTP hoặc mã dự phòng, chống replay" — <see cref="IAuthService"/> chỉ
/// lo phần phiên đăng nhập/token, không tự verify mã.</summary>
public interface ITwoFactorService
{
    Task<TwoFactorStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Sinh secret MỚI (ghi đè secret cũ nếu có) và trả về để hiển thị QR — CHƯA bật 2FA,
    /// phải xác nhận bằng <see cref="EnableAsync"/>.</summary>
    Task<TwoFactorSetupDto> SetupAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Xác nhận mã TOTP hợp lệ rồi mới thật sự bật 2FA — CHỈ chấp nhận mã TOTP (không chấp
    /// nhận mã dự phòng, vì lúc này còn chưa có mã dự phòng nào được sinh). Sinh 10 mã dự phòng mới,
    /// trả về plaintext ĐÚNG 1 LẦN.</summary>
    Task<RecoveryCodesDto> EnableAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken cancellationToken);

    /// <summary>Tắt 2FA — chấp nhận CẢ mã TOTP lẫn mã dự phòng (đây chính là đường lùi khi mất điện
    /// thoại: dùng 1 mã dự phòng còn lại để tắt, rồi thiết lập lại từ đầu với thiết bị mới).</summary>
    Task DisableAsync(Guid userId, DisableTwoFactorRequest request, CancellationToken cancellationToken);

    /// <summary>Sinh lại bộ 10 mã dự phòng (bộ cũ bị vô hiệu hóa toàn bộ) — CHỈ chấp nhận mã TOTP
    /// (phải còn quyền truy cập app xác thực; nếu không còn, dùng DisableAsync bằng mã dự phòng rồi
    /// thiết lập lại thay vì sinh mã mới).</summary>
    Task<RecoveryCodesDto> RegenerateRecoveryCodesAsync(Guid userId, RegenerateRecoveryCodesRequest request, CancellationToken cancellationToken);

    /// <summary>Dùng bởi AuthService khi hoàn tất đăng nhập qua 2FA challenge (CLAUDE.md mục 25.9) —
    /// thử mã TOTP trước (chống replay qua User.TwoFactorLastUsedTimeStep), rồi thử mã dự phòng. Nhận
    /// thẳng entity User đã được tracked bởi cùng DbContext (không tự load lại) để mọi thay đổi
    /// (TwoFactorLastUsedTimeStep, RecoveryCode.UsedAt) được lưu cùng 1 lượt SaveChanges với phần còn
    /// lại của luồng đăng nhập.</summary>
    Task<bool> VerifyCodeOrRecoveryAsync(User user, string code, CancellationToken cancellationToken);
}
