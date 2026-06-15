using core.Application.Identity.Auth.Dtos;
using FluentValidation;

namespace core.Application.Identity.Auth.Validators;

// TARGET convention: validators run as an endpoint filter (ValidationFilter<T>) against
// the REQUEST DTO bound by the route, not the command (which is built in the endpoint with
// ip/ua/brand inputs). Retargeted from the SOURCE command-level validators accordingly.

public sealed class CustomerOtpSendValidator : AbstractValidator<CustomerOtpSendRequest>
{
    public CustomerOtpSendValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Phone must be in E.164 format (e.g. +919876543210).");
    }
}

public sealed class CustomerOtpVerifyValidator : AbstractValidator<CustomerOtpVerifyRequest>
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
