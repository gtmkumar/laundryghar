using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using FluentValidation;
using Microsoft.Extensions.Options;

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

/// <summary>
/// Code length is read from Otp:CustomerCodeLength (4 by default) rather than hardcoded,
/// so changing the setting cannot leave the validator rejecting the codes the send handler
/// actually generates.
/// </summary>
public sealed class CustomerOtpVerifyValidator : AbstractValidator<CustomerOtpVerifyRequest>
{
    public CustomerOtpVerifyValidator(IOptions<OtpSettings> otpOptions)
    {
        // Mirrors the clamp in CustomerOtpSendHandler.GenerateNumericCode.
        var digits = Math.Clamp(otpOptions.Value.CustomerCodeLength, 4, 8);

        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Phone must be in E.164 format.");
        RuleFor(x => x.Code)
            .NotEmpty()
            .Length(digits)
            .Matches($@"^\d{{{digits}}}$")
            .WithMessage($"OTP must be exactly {digits} digits.");
    }
}

public sealed class CustomerGoogleSignInValidator : AbstractValidator<CustomerGoogleSignInRequest>
{
    public CustomerGoogleSignInValidator()
    {
        // Shape-only check. Signature, issuer, audience and expiry are verified by
        // IGoogleIdTokenVerifier — this just rejects obvious junk before the crypto work.
        // The 4096 ceiling is generous for a Google ID token (~1 KB) while bounding the
        // parsing cost of a hostile payload.
        RuleFor(x => x.IdToken)
            .NotEmpty()
            .MaximumLength(4096)
            .Matches(@"^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$")
            .WithMessage("idToken must be a compact-serialization JWT.");
    }
}

public sealed class CustomerPhoneLinkSendValidator : AbstractValidator<CustomerPhoneLinkSendRequest>
{
    public CustomerPhoneLinkSendValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Phone must be in E.164 format (e.g. +919876543210).");
    }
}

public sealed class CustomerPhoneLinkVerifyValidator : AbstractValidator<CustomerPhoneLinkVerifyRequest>
{
    public CustomerPhoneLinkVerifyValidator(IOptions<OtpSettings> otpOptions)
    {
        var digits = Math.Clamp(otpOptions.Value.CustomerCodeLength, 4, 8);

        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{7,14}$")
            .WithMessage("Phone must be in E.164 format.");
        RuleFor(x => x.Code)
            .NotEmpty()
            .Length(digits)
            .Matches($@"^\d{{{digits}}}$")
            .WithMessage($"OTP must be exactly {digits} digits.");
    }
}

/// <summary>
/// PIN policy: 4–6 digits, and not one of the handful of sequences that dominate real-world
/// PIN choices. Rejecting those at set-time is worth more than any extra length rule, because
/// a 5-attempt lockout makes the top few guesses the entire attack surface.
/// </summary>
public sealed class CustomerPinSetValidator : AbstractValidator<CustomerPinSetRequest>
{
    private static readonly string[] Forbidden =
    [
        "0000", "1111", "2222", "3333", "4444", "5555", "6666", "7777", "8888", "9999",
        "1234", "4321", "1212", "0123", "000000", "111111", "123456", "654321"
    ];

    public CustomerPinSetValidator()
    {
        RuleFor(x => x.Pin)
            .NotEmpty()
            .Matches(@"^\d{4,6}$")
            .WithMessage("PIN must be 4 to 6 digits.")
            .Must(pin => !Forbidden.Contains(pin))
            .WithMessage("That PIN is too easy to guess. Please choose another.");
    }
}

public sealed class CustomerPinVerifyValidator : AbstractValidator<CustomerPinVerifyRequest>
{
    public CustomerPinVerifyValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty().MaximumLength(255);
        // Deliberately NOT applying the forbidden-PIN list here: verification must accept
        // whatever the customer actually set, and a rejection pattern would leak policy.
        RuleFor(x => x.Pin).NotEmpty().Matches(@"^\d{4,6}$");
    }
}
