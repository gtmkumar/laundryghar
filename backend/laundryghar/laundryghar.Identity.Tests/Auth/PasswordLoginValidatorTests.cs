using FluentValidation;
using laundryghar.Identity.Application.Auth.Commands;
using laundryghar.Identity.Application.Auth.Validators;

namespace laundryghar.Identity.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="PasswordLoginValidator"/>.
///
/// Key invariant: the login validator's password rules must match the password-SET policy
/// (see <see cref="ResetPasswordValidator"/>) — min 8 chars, one uppercase, one digit —
/// so that credentials that cannot have been legitimately stored are rejected at the gate.
/// </summary>
public sealed class PasswordLoginValidatorTests
{
    private readonly PasswordLoginValidator _sut = new();

    // ── Helper ───────────────────────────────────────────────────────────────

    private static PasswordLoginCommand Cmd(string identifier, string password)
        => new(identifier, password, IpAddress: null, UserAgent: null);

    private bool IsValid(string identifier, string password)
        => _sut.Validate(Cmd(identifier, password)).IsValid;

    private bool HasPasswordError(string identifier, string password)
        => _sut.Validate(Cmd(identifier, password))
               .Errors.Any(e => e.PropertyName == nameof(PasswordLoginCommand.Password));

    private bool HasIdentifierError(string identifier, string password)
        => _sut.Validate(Cmd(identifier, password))
               .Errors.Any(e => e.PropertyName == nameof(PasswordLoginCommand.Identifier));

    // ── Happy path ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("user@example.com", "Admin@123")]     // seeded dev admin password
    [InlineData("+919999999999",    "Secure1Pass")]
    [InlineData("user@example.com", "A1bcdefg")]      // exactly 8 chars
    public void ValidCredentials_Pass(string identifier, string password)
        => Assert.True(IsValid(identifier, password));

    // ── Identifier rules ─────────────────────────────────────────────────────

    [Fact]
    public void EmptyIdentifier_Fails()
        => Assert.True(HasIdentifierError("", "Admin@123"));

    [Fact]
    public void IdentifierExceeds255_Fails()
        => Assert.True(HasIdentifierError(new string('a', 256), "Admin@123"));

    // ── Password minimum length = 8 (aligned to reset/create policy) ─────────

    [Theory]
    [InlineData("A1bcd")]      // 5 chars
    [InlineData("A1bcdef")]    // 7 chars — just below the threshold
    public void PasswordShorterThan8_Fails(string password)
        => Assert.True(HasPasswordError("user@example.com", password));

    // ── Password complexity: uppercase required ───────────────────────────────

    [Fact]
    public void PasswordWithoutUppercase_Fails()
        => Assert.True(HasPasswordError("user@example.com", "alllower1"));

    // ── Password complexity: digit required ──────────────────────────────────

    [Fact]
    public void PasswordWithoutDigit_Fails()
        => Assert.True(HasPasswordError("user@example.com", "NoDigitPass"));

    // ── Password maximum length = 200 ────────────────────────────────────────

    [Fact]
    public void PasswordExceeds200_Fails()
    {
        var longPwd = "A1" + new string('x', 199); // 201 chars
        Assert.True(HasPasswordError("user@example.com", longPwd));
    }

    // ── Seeded dev admin password must not be locked out ─────────────────────

    [Fact]
    public void SeedAdminPassword_Admin123_Passes()
    {
        // "Admin@123" is the default seeded dev admin password (9 chars, 1 upper, 1 digit).
        // This test encodes the invariant that aligning login to 8+complexity does NOT
        // lock out the seeded admin user.
        Assert.True(IsValid("admin@laundryghar.local", "Admin@123"));
    }
}
