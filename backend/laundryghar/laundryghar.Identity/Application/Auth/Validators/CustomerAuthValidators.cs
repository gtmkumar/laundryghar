using FluentValidation;
using laundryghar.Identity.Application.Auth.Commands;

namespace laundryghar.Identity.Application.Auth.Validators;

public sealed class CustomerOtpSendValidator : AbstractValidator<CustomerOtpSendCommand>
{
    public CustomerOtpSendValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Phone must be in E.164 format (e.g. +919876543210).");
    }
}

public sealed class CustomerOtpVerifyValidator : AbstractValidator<CustomerOtpVerifyCommand>
{
    public CustomerOtpVerifyValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Phone must be in E.164 format.");
        RuleFor(x => x.Code)
            .NotEmpty()
            .Length(6)
            .Matches(@"^\d{6}$")
            .WithMessage("OTP must be exactly 6 digits.");
    }
}

public sealed class CustomerRefreshValidator : AbstractValidator<CustomerRefreshCommand>
{
    public CustomerRefreshValidator()
    {
        RuleFor(x => x.RawRefreshToken).NotEmpty();
    }
}

public sealed class CustomerLogoutValidator : AbstractValidator<CustomerLogoutCommand>
{
    public CustomerLogoutValidator()
    {
        RuleFor(x => x.RawRefreshToken).NotEmpty();
    }
}
