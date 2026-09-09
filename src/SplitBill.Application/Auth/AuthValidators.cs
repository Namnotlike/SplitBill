using FluentValidation;

namespace SplitBill.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Email không hợp lệ.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).WithMessage("Mật khẩu phải từ 8 ký tự.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Email không hợp lệ.");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).WithMessage("Mật khẩu phải từ 8 ký tự.");
    }
}

public sealed class GoogleLoginRequestValidator : AbstractValidator<GoogleLoginRequest>
{
    public GoogleLoginRequestValidator()
    {
        RuleFor(x => x.GoogleId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Email không hợp lệ.");
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
    }
}

// ===== Xác thực 2 lớp / TOTP (CLAUDE.md mục 25.9, bổ sung 2026-09-09) =====
public sealed class CompleteTwoFactorLoginRequestValidator : AbstractValidator<CompleteTwoFactorLoginRequest>
{
    public CompleteTwoFactorLoginRequestValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty();
        RuleFor(x => x.Code).NotEmpty();
    }
}

public sealed class EnableTwoFactorRequestValidator : AbstractValidator<EnableTwoFactorRequest>
{
    public EnableTwoFactorRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
    }
}

public sealed class DisableTwoFactorRequestValidator : AbstractValidator<DisableTwoFactorRequest>
{
    public DisableTwoFactorRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
    }
}

public sealed class RegenerateRecoveryCodesRequestValidator : AbstractValidator<RegenerateRecoveryCodesRequest>
{
    public RegenerateRecoveryCodesRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
    }
}
