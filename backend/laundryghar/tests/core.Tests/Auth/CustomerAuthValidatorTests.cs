using core.Application.Identity.Auth.Dtos;
using core.Application.Identity.Auth.Validators;
using Xunit;

namespace core.Tests.Auth;

/// <summary>
/// Input gates for the new sign-in surfaces. These validators are the cheap first pass —
/// the real checks are the Google JWKS verification and the PIN lockout — but they must
/// not reject anything legitimate, since a false rejection is a login nobody can complete.
/// </summary>
public class CustomerAuthValidatorTests
{
    // ── Google ID token shape ────────────────────────────────────────────────

    [Fact]
    public void Google_validator_accepts_a_compact_jwt()
    {
        // Base64url segments, including the '-' and '_' characters that distinguish
        // base64url from base64 — rejecting those would break real Google tokens.
        var token = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjM0-_abc.c2lnbmF0dXJl-_xyz";

        Assert.True(new CustomerGoogleSignInValidator()
            .Validate(new CustomerGoogleSignInRequest(token)).IsValid);
    }

    [Theory]
    [InlineData("")]                              // empty
    [InlineData("not-a-jwt")]                     // no segments
    [InlineData("only.two")]                      // too few segments
    [InlineData("a.b.c.d")]                       // too many segments
    [InlineData("eyJhbGci.payload.sig nature")]   // whitespace
    [InlineData("eyJhbGci.pay+load.sig")]         // '+' is base64, not base64url
    public void Google_validator_rejects_malformed_tokens(string token)
    {
        Assert.False(new CustomerGoogleSignInValidator()
            .Validate(new CustomerGoogleSignInRequest(token)).IsValid);
    }

    [Fact]
    public void Google_validator_rejects_an_oversized_token()
    {
        // Bounds the parsing cost of a hostile payload; a real Google ID token is ~1 KB.
        var huge = $"{new string('a', 4100)}.{new string('b', 10)}.{new string('c', 10)}";

        Assert.False(new CustomerGoogleSignInValidator()
            .Validate(new CustomerGoogleSignInRequest(huge)).IsValid);
    }

    // ── PIN policy ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2571")]
    [InlineData("90210")]
    [InlineData("284916")]
    public void Pin_validator_accepts_four_to_six_non_trivial_digits(string pin)
    {
        Assert.True(new CustomerPinSetValidator().Validate(new CustomerPinSetRequest(pin)).IsValid);
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("1111")]
    [InlineData("1234")]
    [InlineData("4321")]
    [InlineData("123456")]
    public void Pin_validator_rejects_the_most_guessed_pins(string pin)
    {
        // With a 5-attempt lockout, the handful of dominant real-world PINs ARE the
        // attack surface — blocking them at set-time is worth more than extra length.
        var result = new CustomerPinSetValidator().Validate(new CustomerPinSetRequest(pin));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("123")]        // too short
    [InlineData("1234567")]    // too long
    [InlineData("12a4")]       // non-numeric
    [InlineData("")]
    public void Pin_validator_rejects_wrong_shapes(string pin)
    {
        Assert.False(new CustomerPinSetValidator().Validate(new CustomerPinSetRequest(pin)).IsValid);
    }

    [Fact]
    public void Pin_verify_does_not_apply_the_forbidden_list()
    {
        // Verification must accept whatever was actually set. Applying the policy here
        // would both break legacy PINs and leak the policy to an attacker.
        var result = new CustomerPinVerifyValidator()
            .Validate(new CustomerPinVerifyRequest("+919876543210", "1234"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Pin_verify_accepts_an_email_identifier()
    {
        // A Google-first customer may have no phone number at all, so email must work
        // as the unlock identifier.
        var result = new CustomerPinVerifyValidator()
            .Validate(new CustomerPinVerifyRequest("asha@example.com", "2571"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Pin_verify_requires_an_identifier()
    {
        Assert.False(new CustomerPinVerifyValidator()
            .Validate(new CustomerPinVerifyRequest("", "2571")).IsValid);
    }

    // ── Phone link ───────────────────────────────────────────────────────────

    [Fact]
    public void Phone_link_send_requires_e164()
    {
        var validator = new CustomerPhoneLinkSendValidator();

        Assert.True(validator.Validate(new CustomerPhoneLinkSendRequest("+919876543210")).IsValid);
        Assert.False(validator.Validate(new CustomerPhoneLinkSendRequest("9876543210")).IsValid);
    }
}
