using FluentValidation;
using laundryghar.Identity.Application.Auth.Commands;

namespace laundryghar.Identity.Application.Auth.Validators;

public sealed class PasswordLoginValidator : AbstractValidator<PasswordLoginCommand>
{
    public PasswordLoginValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).MaximumLength(200);
    }
}

public sealed class OtpSendValidator : AbstractValidator<OtpSendCommand>
{
    private static readonly string[] ValidTypes    = ["phone", "email"];
    private static readonly string[] ValidPurposes = [
        OtpPurpose.Login, OtpPurpose.Signup, OtpPurpose.VerifyPhone,
        OtpPurpose.VerifyEmail, OtpPurpose.ResetPassword
    ];

    public OtpSendValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IdentifierType).NotEmpty().Must(t => ValidTypes.Contains(t))
            .WithMessage("identifierType must be 'phone' or 'email'.");
        RuleFor(x => x.Purpose).NotEmpty().Must(p => ValidPurposes.Contains(p))
            .WithMessage("Invalid OTP purpose.");
    }
}

public sealed class OtpVerifyValidator : AbstractValidator<OtpVerifyCommand>
{
    private static readonly string[] ValidTypes = ["phone", "email"];

    public OtpVerifyValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IdentifierType).NotEmpty().Must(t => ValidTypes.Contains(t));
        RuleFor(x => x.Purpose).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches(@"^\d{6}$")
            .WithMessage("OTP must be exactly 6 digits.");
    }
}

public sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator()
    {
        RuleFor(x => x.RawRefreshToken).NotEmpty();
    }
}

public sealed class LogoutValidator : AbstractValidator<LogoutCommand>
{
    public LogoutValidator()
    {
        RuleFor(x => x.RawRefreshToken).NotEmpty();
    }
}

public sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    private static readonly string[] ValidTypes = ["phone", "email"];

    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        RuleFor(x => x.IdentifierType).NotEmpty().Must(t => ValidTypes.Contains(t));
    }
}

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(200)
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one digit.");
    }
}
