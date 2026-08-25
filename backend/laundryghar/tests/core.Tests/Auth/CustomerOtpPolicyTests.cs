using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using core.Application.Identity.Auth.Validators;
using Microsoft.Extensions.Options;
using Xunit;

namespace core.Tests.Auth;

/// <summary>
/// Guards the 4-digit customer OTP switch. The dangerous failure mode here is a mismatch
/// between what the send handler generates and what the validator accepts — that locks
/// every customer out with a "code must be N digits" error nobody can satisfy.
/// </summary>
public class CustomerOtpPolicyTests
{
    private static CustomerOtpVerifyValidator VerifyValidator(int codeLength) =>
        new(Options.Create(new OtpSettings { CustomerCodeLength = codeLength }));

    [Fact]
    public void Customer_otp_defaults_to_four_digits_and_staff_stays_at_six()
    {
        var settings = new OtpSettings();

        Assert.Equal(4, settings.CustomerCodeLength);
        Assert.Equal(6, settings.StaffCodeLength);
    }

    [Fact]
    public void Verify_validator_accepts_a_four_digit_code_by_default()
    {
        var result = VerifyValidator(4).Validate(
            new CustomerOtpVerifyRequest("+919876543210", "1234"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("123")]      // too short
    [InlineData("12345")]    // too long
    [InlineData("123456")]   // the old staff-length code
    [InlineData("12a4")]     // non-numeric
    [InlineData("")]
    public void Verify_validator_rejects_codes_that_are_not_four_digits(string code)
    {
        var result = VerifyValidator(4).Validate(
            new CustomerOtpVerifyRequest("+919876543210", code));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verify_validator_follows_a_reconfigured_code_length()
    {
        // The length is read from configuration precisely so this stays in step with
        // the send handler rather than being hardcoded in two places.
        var validator = VerifyValidator(6);

        Assert.True(validator.Validate(new CustomerOtpVerifyRequest("+919876543210", "123456")).IsValid);
        Assert.False(validator.Validate(new CustomerOtpVerifyRequest("+919876543210", "1234")).IsValid);
    }

    [Fact]
    public void Verify_validator_clamps_an_absurd_configured_length()
    {
        // Mirrors GenerateNumericCode's clamp: a config typo must not produce a validator
        // that no generated code can ever satisfy.
        var result = VerifyValidator(99).Validate(
            new CustomerOtpVerifyRequest("+919876543210", "12345678"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("9876543210")]        // missing country code
    [InlineData("+9198")]             // below the 8-digit E.164 minimum
    [InlineData("+91987654321012345")] // above the 15-digit E.164 maximum
    [InlineData("+0919876543210")]    // leading zero in the country code
    public void Verify_validator_rejects_non_e164_phones(string phone)
    {
        Assert.False(VerifyValidator(4).Validate(new CustomerOtpVerifyRequest(phone, "1234")).IsValid);
    }

    // ── Test master code ─────────────────────────────────────────────────────

    [Fact]
    public void Customer_test_code_is_accepted_outside_production()
    {
        Assert.True(OtpSecurityHelper.IsTestCodeAccepted("1234", isProduction: false, "1234"));
    }

    [Fact]
    public void Customer_test_code_is_refused_in_production()
    {
        // Defence in depth: the host also refuses to start with Otp:CustomerTestCode set
        // in Production, but the verify path must fail closed on its own.
        Assert.False(OtpSecurityHelper.IsTestCodeAccepted("1234", isProduction: true, "1234"));
    }

    [Fact]
    public void A_different_code_is_not_accepted_as_the_test_code()
    {
        Assert.False(OtpSecurityHelper.IsTestCodeAccepted("1234", isProduction: false, "4321"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_configured_test_code_means_no_master_code(string? testCode)
    {
        Assert.False(OtpSecurityHelper.IsTestCodeAccepted(testCode, isProduction: false, "1234"));
    }

    [Fact]
    public void Customer_and_staff_test_codes_are_separate_settings()
    {
        // The customer flow must not accept the 6-digit staff master code — different
        // length, different audience, and the customer validator would reject it anyway.
        var settings = new OtpSettings { TestCode = "123456", CustomerTestCode = "1234" };

        Assert.NotEqual(settings.TestCode, settings.CustomerTestCode);
        Assert.False(OtpSecurityHelper.IsTestCodeAccepted(settings.CustomerTestCode, false, "123456"));
    }
}
