using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SplitBill.Application.Auth;
using SplitBill.Application.Common;

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

    public AuthController(
        IAuthService authService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<ForgotPasswordRequest> forgotPasswordValidator,
        IValidator<ResetPasswordRequest> resetPasswordValidator)
    {
        _authService = authService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _forgotPasswordValidator = forgotPasswordValidator;
        _resetPasswordValidator = resetPasswordValidator;
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
}
