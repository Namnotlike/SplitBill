using Microsoft.AspNetCore.Identity;
using SplitBill.Application.Abstractions;
using SplitBill.Domain.Entities;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Application.Auth;

/// <summary>Cài đặt `/auth/*` (CLAUDE.md mục 8). Mật khẩu băm bằng PasswordHasher&lt;User&gt; (mục 2).</summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _jwtTokenGenerator = jwtTokenGenerator;
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

    public async Task<AuthTokens> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
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

        return await IssueTokensAsync(user, cancellationToken);
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
