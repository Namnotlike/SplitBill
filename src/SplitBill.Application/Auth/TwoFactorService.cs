using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Auth;

/// <summary>Cài đặt CLAUDE.md mục 25.9.</summary>
public sealed class TwoFactorService : ITwoFactorService
{
    private const int RecoveryCodeCount = 10;

    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorRecoveryCodeRepository _recoveryCodeRepository;
    private readonly ITotpService _totpService;
    private readonly ITwoFactorSecretProtector _secretProtector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TwoFactorService> _logger;

    public TwoFactorService(
        IUserRepository userRepository,
        ITwoFactorRecoveryCodeRepository recoveryCodeRepository,
        ITotpService totpService,
        ITwoFactorSecretProtector secretProtector,
        IUnitOfWork unitOfWork,
        ILogger<TwoFactorService> logger)
    {
        _userRepository = userRepository;
        _recoveryCodeRepository = recoveryCodeRepository;
        _totpService = totpService;
        _secretProtector = secretProtector;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TwoFactorStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await LoadUserAsync(userId, cancellationToken);
        return new TwoFactorStatusDto(user.TwoFactorEnabled);
    }

    public async Task<TwoFactorSetupDto> SetupAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await LoadUserAsync(userId, cancellationToken);
        var secret = _totpService.GenerateSecret();

        // (Tái) thiết lập luôn ghi đè secret cũ + về trạng thái CHƯA bật — buộc xác nhận lại bằng
        // EnableAsync trước khi 2FA thật sự có hiệu lực với secret mới, tránh 1 secret "treo" chưa ai
        // xác nhận vẫn được coi là đang bảo vệ tài khoản.
        user.TwoFactorSecretEncrypted = _secretProtector.Protect(secret);
        user.TwoFactorEnabled = false;
        user.TwoFactorLastUsedTimeStep = null;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var uri = _totpService.BuildOtpAuthUri(secret, user.Email ?? user.DisplayName);
        return new TwoFactorSetupDto(secret, uri);
    }

    public async Task<RecoveryCodesDto> EnableAsync(Guid userId, EnableTwoFactorRequest request, CancellationToken cancellationToken)
    {
        var user = await LoadUserAsync(userId, cancellationToken);
        if (user.TwoFactorSecretEncrypted is null)
        {
            throw new DomainException(ErrorCodes.TwoFactorSetupRequired, "Chưa thiết lập 2FA — gọi setup trước.");
        }

        if (user.TwoFactorEnabled)
        {
            throw new DomainException(ErrorCodes.TwoFactorAlreadyEnabled, "2FA đã được bật rồi.");
        }

        var secret = UnprotectSecretOrThrow(user.TwoFactorSecretEncrypted);
        var matchedStep = _totpService.TryMatchTimeStep(secret, request.Code, DateTimeOffset.UtcNow);
        if (matchedStep is null)
        {
            throw new DomainException(ErrorCodes.InvalidTwoFactorCode, "Mã xác thực không đúng.");
        }

        user.TwoFactorEnabled = true;
        user.TwoFactorLastUsedTimeStep = matchedStep;

        var recoveryCodes = await ReplaceRecoveryCodesAsync(userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new RecoveryCodesDto(recoveryCodes);
    }

    public async Task DisableAsync(Guid userId, DisableTwoFactorRequest request, CancellationToken cancellationToken)
    {
        var user = await LoadUserAsync(userId, cancellationToken);
        if (!user.TwoFactorEnabled)
        {
            throw new DomainException(ErrorCodes.TwoFactorNotEnabled, "2FA chưa được bật.");
        }

        var valid = await VerifyCodeOrRecoveryAsync(user, request.Code, cancellationToken);
        if (!valid)
        {
            throw new DomainException(ErrorCodes.InvalidTwoFactorCode, "Mã xác thực không đúng.");
        }

        user.TwoFactorEnabled = false;
        user.TwoFactorSecretEncrypted = null;
        user.TwoFactorLastUsedTimeStep = null;
        await _recoveryCodeRepository.RemoveAllForUserAsync(userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<RecoveryCodesDto> RegenerateRecoveryCodesAsync(Guid userId, RegenerateRecoveryCodesRequest request, CancellationToken cancellationToken)
    {
        var user = await LoadUserAsync(userId, cancellationToken);
        if (!user.TwoFactorEnabled)
        {
            throw new DomainException(ErrorCodes.TwoFactorNotEnabled, "2FA chưa được bật.");
        }

        // CHỈ chấp nhận TOTP (không phải mã dự phòng) — xem doc comment ITwoFactorService.
        var secret = UnprotectSecretOrThrow(user.TwoFactorSecretEncrypted!);
        var matchedStep = _totpService.TryMatchTimeStep(secret, request.Code, DateTimeOffset.UtcNow);
        if (matchedStep is null || matchedStep == user.TwoFactorLastUsedTimeStep)
        {
            throw new DomainException(ErrorCodes.InvalidTwoFactorCode, "Mã xác thực không đúng.");
        }

        user.TwoFactorLastUsedTimeStep = matchedStep;
        var recoveryCodes = await ReplaceRecoveryCodesAsync(userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new RecoveryCodesDto(recoveryCodes);
    }

    public async Task<bool> VerifyCodeOrRecoveryAsync(User user, string code, CancellationToken cancellationToken)
    {
        if (user.TwoFactorSecretEncrypted is not null)
        {
            // Cố tình KHÔNG dùng UnprotectSecretOrThrow (ném DomainException) ở đây — khác EnableAsync/
            // RegenerateRecoveryCodesAsync (chỉ có đúng 1 đường TOTP, không có lối thoát nào khác), hàm
            // này còn đường dự phòng THẬT SỰ (mã dự phòng) ngay bên dưới. Nếu Unprotect lỗi (ví dụ
            // TwoFactor:EncryptionKey đã bị đổi sau khi user bật 2FA — xem CLAUDE.md mục 25.9), coi như
            // nhánh TOTP "không dùng được" và rơi thẳng xuống thử mã dự phòng, thay vì ném lỗi chặn đứng
            // luôn cả đường dự phòng — nếu không, đúng cái an toàn (mã dự phòng) được thiết kế riêng cho
            // tình huống "mất khả năng dùng TOTP" lại bị chính lỗi hạ tầng này vô hiệu hóa theo.
            try
            {
                var secret = _secretProtector.Unprotect(user.TwoFactorSecretEncrypted);
                var matchedStep = _totpService.TryMatchTimeStep(secret, code, DateTimeOffset.UtcNow);
                if (matchedStep is not null && matchedStep != user.TwoFactorLastUsedTimeStep)
                {
                    user.TwoFactorLastUsedTimeStep = matchedStep;
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return true;
                }
            }
            catch (CryptographicException)
            {
                // Bỏ qua nhánh TOTP, thử mã dự phòng bên dưới — xem lý do ở comment trên. VẪN log lại
                // (khác EnableAsync/RegenerateRecoveryCodesAsync, vốn báo lỗi thẳng cho client) vì
                // nhánh này rơi êm về "sai mã" (401 thông thường) — nếu không log, operator không có
                // cách nào phân biệt "user gõ sai mã" với "TwoFactor:EncryptionKey đã bị đổi" khi có
                // báo cáo hàng loạt user không đăng nhập được (CLAUDE.md mục 25.9).
                _logger.LogWarning(
                    "Không giải mã được TwoFactorSecretEncrypted của user {UserId} — có thể TwoFactor:EncryptionKey đã thay đổi. Đã rơi về thử mã dự phòng.",
                    user.Id);
            }
        }

        var hash = HashRecoveryCode(code);
        var recoveryCode = await _recoveryCodeRepository.GetByHashAsync(hash, cancellationToken);
        if (recoveryCode is not null && recoveryCode.UserId == user.Id && recoveryCode.IsUsable)
        {
            recoveryCode.UsedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<List<string>> ReplaceRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _recoveryCodeRepository.RemoveAllForUserAsync(userId, cancellationToken);
        var recoveryCodes = GenerateRecoveryCodes();
        await _recoveryCodeRepository.AddRangeAsync(recoveryCodes.Select(c => new TwoFactorRecoveryCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CodeHash = HashRecoveryCode(c),
            CreatedAt = DateTimeOffset.UtcNow,
        }), cancellationToken);
        return recoveryCodes;
    }

    /// <summary>Dùng cho các luồng CHỈ CÓ đúng 1 đường TOTP, không có mã dự phòng thay thế
    /// (EnableAsync/RegenerateRecoveryCodesAsync — mã dự phòng cũ đã bị vô hiệu/chưa từng tồn tại ở 2
    /// bước này). Giải mã lỗi (vd <see cref="TwoFactorOptions.EncryptionKey"/> đã đổi sau khi secret
    /// được mã hóa — CLAUDE.md mục 25.9) ném <see cref="ErrorCodes.TwoFactorDecryptionFailed"/> thay vì
    /// để <see cref="CryptographicException"/> lọt ra ngoài thành lỗi 500 chung chung không rõ nguyên
    /// nhân.</summary>
    private string UnprotectSecretOrThrow(string encryptedSecret)
    {
        try
        {
            return _secretProtector.Unprotect(encryptedSecret);
        }
        catch (CryptographicException)
        {
            throw new DomainException(
                ErrorCodes.TwoFactorDecryptionFailed,
                "Không thể giải mã bí mật 2FA — có thể cấu hình máy chủ đã thay đổi. Liên hệ quản trị viên.");
        }
    }

    private async Task<User> LoadUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.InvalidCredentials, "Tài khoản không tồn tại.");

    private static List<string> GenerateRecoveryCodes() =>
        Enumerable.Range(0, RecoveryCodeCount)
            .Select(_ =>
            {
                var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(); // 10 hex ký tự
                return $"{raw[..4]}-{raw[4..8]}-{raw[8..]}"; // "ab12-cd34-ef" — dễ đọc/chép tay hơn 1 chuỗi liền
            })
            .ToList();

    private static string NormalizeRecoveryCode(string code) => code.Trim().ToLowerInvariant().Replace("-", "");

    private static string HashRecoveryCode(string code) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeRecoveryCode(code))));
}
