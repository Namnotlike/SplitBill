using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SplitBill.Application.Auth;
using SplitBill.Application.Common;

namespace SplitBill.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthController(IAuthService authService, IValidator<RegisterRequest> registerValidator, IValidator<LoginRequest> loginValidator)
    {
        _authService = authService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
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
}
