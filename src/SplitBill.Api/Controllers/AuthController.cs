using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SplitBill.Application.Auth;
using SplitBill.Application.Common;
using SplitBill.Domain.Exceptions;

namespace SplitBill.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<ForgotPasswordRequest> _forgotPasswordValidator;
    private readonly IValidator<ResetPasswordRequest> _resetPasswordValidator;
    private readonly IValidator<GoogleLoginRequest> _googleLoginValidator;
    private readonly GoogleAuthOptions _googleAuthOptions;

    public AuthController(
        IAuthService authService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<ForgotPasswordRequest> forgotPasswordValidator,
        IValidator<ResetPasswordRequest> resetPasswordValidator,
        IValidator<GoogleLoginRequest> googleLoginValidator,
        IOptions<GoogleAuthOptions> googleAuthOptions)
    {
        _authService = authService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _forgotPasswordValidator = forgotPasswordValidator;
        _resetPasswordValidator = resetPasswordValidator;
        _googleLoginValidator = googleLoginValidator;
        _googleAuthOptions = googleAuthOptions.Value;
    }

    public sealed record RefreshRequest(string RefreshToken);

    [HttpPost("register")]
    public async Task<ActionResult<AuthTokens>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        _registerValidator.ValidateOrThrowDomainException(request);
        var tokens = await _authService.RegisterAsync(request, cancellationToken);
        return Ok(tokens);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthTokens>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        _loginValidator.ValidateOrThrowDomainException(request);
        var tokens = await _authService.LoginAsync(request, cancellationToken);
        return Ok(tokens);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthTokens>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var tokens = await _authService.RefreshAsync(request.RefreshToken, cancellationToken);
        return Ok(tokens);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    // CLAUDE.md mục 16 — Quên mật khẩu (bổ sung 2026-09-07).
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        _forgotPasswordValidator.ValidateOrThrowDomainException(request);
        // Luôn trả 204 dù email có tồn tại hay không — không tiết lộ (CLAUDE.md mục 8).
        await _authService.ForgotPasswordAsync(request.Email, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        _resetPasswordValidator.ValidateOrThrowDomainException(request);
        await _authService.ResetPasswordAsync(request, cancellationToken);
        return NoContent();
    }

    // CLAUDE.md mục 25.3 — Đăng nhập bằng Google (bổ sung 2026-09-09). CHỈ được gọi từ SplitBill.Web
    // (đã tự xác thực toàn bộ luồng OAuth với Google TRƯỚC khi gọi tới đây) — bắt buộc header
    // X-Internal-Secret khớp GoogleAuthOptions.InternalSecret, nếu không endpoint này sẽ là 1 lỗ hổng
    // chiếm tài khoản (ai cũng gọi thẳng được với 1 email tùy ý). Xem GoogleAuthOptions để biết đầy đủ
    // lý do thiết kế.
    [HttpPost("google")]
    public async Task<ActionResult<AuthTokens>> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var providedSecret = Request.Headers["X-Internal-Secret"].ToString();
        if (!InternalSecretComparer.Matches(providedSecret, _googleAuthOptions.InternalSecret))
        {
            throw new DomainException(ErrorCodes.InvalidInternalSecret, "Không xác thực được nguồn gọi.");
        }

        _googleLoginValidator.ValidateOrThrowDomainException(request);
        var tokens = await _authService.GoogleLoginAsync(request, cancellationToken);
        return Ok(tokens);
    }
}
