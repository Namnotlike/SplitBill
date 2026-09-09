using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SplitBill.Application.Abstractions;
using SplitBill.Application.Common;
using SplitBill.Application.Notifications;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Auth;

/// <summary>Cài đặt `/auth/*` (CLAUDE.md mục 8). Mật khẩu băm bằng PasswordHasher&lt;User&gt; (mục 2).</summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly ITwoFactorChallengeRepository _twoFactorChallengeRepository;
    private readonly ITwoFactorService _twoFactorService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly WebOptions _webOptions;
    private readonly ILogger<AuthService> _logger;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        ITwoFactorChallengeRepository twoFactorChallengeRepository,
        ITwoFactorService twoFactorService,
        IUnitOfWork unitOfWork,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailSender emailSender,
        IOptions<WebOptions> webOptions,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _twoFactorChallengeRepository = twoFactorChallengeRepository;
        _twoFactorService = twoFactorService;
        _unitOfWork = unitOfWork;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailSender = emailSender;
        _webOptions = webOptions.Value;
        _logger = logger;
    }

    public async Task<AuthTokens> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var existing = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
        {
            throw new DomainException(ErrorCodes.EmailAlreadyRegistered, "Email đã được đăng ký.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        await _userRepository.AddAsync(user, cancellationToken);

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (user?.PasswordHash is null)
        {
            // Không tiết lộ email có tồn tại hay không (CLAUDE.md mục 8).
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email hoặc mật khẩu không đúng.");
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email hoặc mật khẩu không đúng.");
        }

        return await CompleteLoginAsync(user, cancellationToken);
    }

    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = _jwtTokenGenerator.HashRefreshToken(refreshToken);
        var existing = await _refreshTokenRepository.GetByHashAsync(hash, cancellationToken);
        if (existing is null || !existing.IsActive)
        {
            throw new DomainException(ErrorCodes.InvalidRefreshToken, "Refresh token không hợp lệ hoặc đã hết hạn.");
        }

        var user = await _userRepository.GetByIdAsync(existing.UserId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.InvalidRefreshToken, "Tài khoản không tồn tại.");

        // Rotation: thu hồi token cũ trước khi phát token mới (CLAUDE.md mục 2).
        existing.RevokedAt = DateTimeOffset.UtcNow;

        var tokens = await IssueTokensAsync(user, cancellationToken);
        return tokens;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = _jwtTokenGenerator.HashRefreshToken(refreshToken);
        var existing = await _refreshTokenRepository.GetByHashAsync(hash, cancellationToken);
        if (existing is not null && existing.RevokedAt is null)
        {
            existing.RevokedAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    // Sàn thời gian tối thiểu cho ForgotPasswordAsync — chặn timing side-channel dò email tồn tại
    // (xem ghi chú chi tiết bên dưới).
    private static readonly TimeSpan ForgotPasswordMinDuration = TimeSpan.FromMilliseconds(500);

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken)
    {
        // Endpoint này luôn trả 204 và không tiết lộ gì qua NỘI DUNG response dù email có tồn tại hay
        // không (CLAUDE.md mục 8/16) — nhưng nhánh "email tồn tại" (ghi PasswordResetToken vào DB +
        // gửi email, với SmtpEmailSender là cả 1 phiên SMTP thật qua mạng) chậm hơn hẳn nhánh "không
        // tồn tại/khách vãng lai" (chỉ 1 câu SELECT rồi trả về ngay) — tạo ra kênh rò rỉ qua ĐỘ TRỄ
        // response, phát hiện qua security-review (2026-09-08). Áp sàn thời gian tối thiểu CHUNG cho
        // cả 2 nhánh bằng Task.WhenAll với Task.Delay: nhánh nhanh luôn "chờ thêm" cho đủ sàn, nhánh
        // chậm hiếm khi bị ảnh hưởng vì thường đã tốn hơn sàn này. Không loại bỏ tuyệt đối kênh rò rỉ
        // (email gửi chậm bất thường vẫn có thể lộ), nhưng đưa case điển hình về gần như không phân
        // biệt được — mức giảm thiểu thực tế/phổ biến cho lớp lỗi này, không cần đổi luồng gửi email
        // sang fire-and-forget (vốn kéo theo rủi ro CancellationToken của request bị hủy giữa chừng
        // làm email không gửi được, và phải tự quản lý DI scope mới).
        var work = ForgotPasswordCoreAsync(email, cancellationToken);
        var minDuration = Task.Delay(ForgotPasswordMinDuration, cancellationToken);
        await Task.WhenAll(work, minDuration);
    }

    private async Task ForgotPasswordCoreAsync(string email, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        // Guest (PasswordHash null) không đăng nhập được nên cũng không đặt lại mật khẩu được — coi
        // như "không tìm thấy", vẫn không tiết lộ gì ra ngoài (CLAUDE.md mục 8).
        if (user?.PasswordHash is null)
        {
            return;
        }

        var (plaintext, hash, expiresAt) = _jwtTokenGenerator.GeneratePasswordResetToken();
        await _passwordResetTokenRepository.AddAsync(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Gửi email KHÔNG được làm hỏng luồng chính (cùng nguyên tắc NotificationService mục 13.3) —
        // token đã lưu DB dù email lỗi, người dùng có thể yêu cầu gửi lại.
        try
        {
            var resetLink = $"{_webOptions.BaseUrl.TrimEnd('/')}/Account/ResetPassword?token={Uri.EscapeDataString(plaintext)}";
            var minutes = expiresAt.Subtract(DateTimeOffset.UtcNow).TotalMinutes;
            var body = $"<p>Bạn (hoặc ai đó) vừa yêu cầu đặt lại mật khẩu cho tài khoản SplitBill này.</p>"
                + $"<p><a href=\"{resetLink}\">Đặt lại mật khẩu</a></p>"
                + $"<p>Link có hiệu lực trong khoảng {Math.Round(minutes)} phút. Nếu không phải bạn yêu cầu, hãy bỏ qua email này.</p>";
            await _emailSender.SendAsync(user.Email!, "Đặt lại mật khẩu SplitBill", body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không gửi được email đặt lại mật khẩu tới {Email}", user.Email);
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var hash = _jwtTokenGenerator.HashRefreshToken(request.Token);
        var token = await _passwordResetTokenRepository.GetByHashAsync(hash, cancellationToken);
        if (token is null || !token.IsUsable)
        {
            throw new DomainException(ErrorCodes.InvalidResetToken, "Link đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
        }

        var user = await _userRepository.GetByIdAsync(token.UserId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.InvalidResetToken, "Tài khoản không tồn tại.");

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        token.UsedAt = DateTimeOffset.UtcNow; // dùng 1 lần — dù còn hạn cũng không dùng lại được

        // Đổi mật khẩu = đăng xuất mọi phiên khác (phòng mật khẩu cũ đã bị lộ, đây chính là kịch bản
        // "quên/lộ mật khẩu" nên luôn xử lý bảo thủ).
        await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoginResult> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByGoogleIdAsync(request.GoogleId, cancellationToken);
        if (user is null)
        {
            user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
            if (user is not null)
            {
                // Email này đã có tài khoản (đăng ký bằng mật khẩu từ trước) — Google đã xác thực
                // chủ sở hữu email này (OAuth) nên tự liên kết an toàn, không tạo User trùng lặp. Từ
                // đây user có thể đăng nhập bằng CẢ HAI cách (mật khẩu cũ hoặc Google).
                user.GoogleId = request.GoogleId;
            }
            else
            {
                user = new User
                {
                    Id = Guid.NewGuid(),
                    Email = request.Email,
                    DisplayName = request.DisplayName,
                    GoogleId = request.GoogleId,
                    PasswordHash = null, // chỉ đăng nhập được qua Google, chưa từng đặt mật khẩu
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                await _userRepository.AddAsync(user, cancellationToken);
            }
        }

        return await CompleteLoginAsync(user, cancellationToken);
    }

    public async Task<AuthTokens> CompleteTwoFactorLoginAsync(CompleteTwoFactorLoginRequest request, CancellationToken cancellationToken)
    {
        var hash = _jwtTokenGenerator.HashRefreshToken(request.ChallengeToken);
        var challenge = await _twoFactorChallengeRepository.GetByHashAsync(hash, cancellationToken);
        if (challenge is null || !challenge.IsUsable)
        {
            throw new DomainException(ErrorCodes.InvalidTwoFactorChallenge, "Yêu cầu đăng nhập đã hết hạn hoặc không hợp lệ, vui lòng đăng nhập lại.");
        }

        var user = await _userRepository.GetByIdAsync(challenge.UserId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.InvalidTwoFactorChallenge, "Tài khoản không tồn tại.");

        var valid = await _twoFactorService.VerifyCodeOrRecoveryAsync(user, request.Code, cancellationToken);
        if (!valid)
        {
            throw new DomainException(ErrorCodes.InvalidTwoFactorCode, "Mã xác thực không đúng.");
        }

        challenge.UsedAt = DateTimeOffset.UtcNow; // dùng 1 lần — dù còn hạn cũng không dùng lại được
        return await IssueTokensAsync(user, cancellationToken);
    }

    /// <summary>Điểm hội tụ DUY NHẤT cho mọi luồng đăng nhập (email/mật khẩu + Google, CLAUDE.md mục
    /// 25.9) — kiểm tra 2FA rồi hoặc phát token thật ngay, hoặc trả về 1 challenge chờ mã 2FA. KHÔNG
    /// dùng cho RefreshAsync (refresh token không phải "đăng nhập mới", không re-trigger 2FA).</summary>
    private async Task<LoginResult> CompleteLoginAsync(User user, CancellationToken cancellationToken)
    {
        if (!user.TwoFactorEnabled)
        {
            var tokens = await IssueTokensAsync(user, cancellationToken);
            return new LoginResult(false, tokens, null);
        }

        var (plaintext, hash, expiresAt) = _jwtTokenGenerator.GenerateTwoFactorChallengeToken();
        await _twoFactorChallengeRepository.AddAsync(new TwoFactorChallenge
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new LoginResult(true, null, plaintext);
    }

    private async Task<AuthTokens> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var (accessToken, accessExpiresAt) = _jwtTokenGenerator.GenerateAccessToken(user);
        var (refreshPlaintext, refreshHash, refreshExpiresAt) = _jwtTokenGenerator.GenerateRefreshToken();

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthTokens(accessToken, accessExpiresAt, refreshPlaintext, refreshExpiresAt);
    }
}
