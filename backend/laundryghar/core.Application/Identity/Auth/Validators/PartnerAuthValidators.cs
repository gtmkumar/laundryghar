using core.Application.Identity.Auth.Dtos;
using FluentValidation;

namespace core.Application.Identity.Auth.Validators;

// Validators run as an endpoint filter (ValidationFilter<T>) against the REQUEST DTO bound by the
// route, mirroring the customer-auth lane. Registered by AddValidatorsFromAssembly in core.Application.

public sealed class PartnerOtpSendValidator : AbstractValidator<PartnerOtpSendRequest>
{
    public PartnerOtpSendValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Phone must be in E.164 format (e.g. +919876543210).");
    }
}

public sealed class PartnerOtpVerifyValidator : AbstractValidator<PartnerOtpVerifyRequest>
{
    public PartnerOtpVerifyValidator()
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
