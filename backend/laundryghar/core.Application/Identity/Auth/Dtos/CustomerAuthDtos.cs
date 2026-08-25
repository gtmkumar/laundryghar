namespace core.Application.Identity.Auth.Dtos;

// ─── Customer auth requests ─────────────────────────────────────────────────

/// <param name="Phone">E.164 phone number, e.g. +919876543210</param>
/// <param name="BrandCode">Optional; resolved from X-Brand-Id header if omitted.</param>
public sealed record CustomerOtpSendRequest(string Phone, string? BrandCode = null);

public sealed record CustomerOtpVerifyRequest(string Phone, string Code, string? BrandCode = null);

/// <summary>Google sign-in — the client obtains the ID token from Google and posts it here.</summary>
/// <param name="IdToken">A Google-issued OpenID Connect ID token (JWT).</param>
public sealed record CustomerGoogleSignInRequest(string IdToken, string? BrandCode = null);

/// <summary>Attach a phone number to the signed-in customer — step 1, send the OTP.</summary>
public sealed record CustomerPhoneLinkSendRequest(string Phone);

/// <summary>Attach a phone number to the signed-in customer — step 2, verify the OTP.</summary>
public sealed record CustomerPhoneLinkVerifyRequest(string Phone, string Code);

/// <summary>Set or replace the signed-in customer's unlock PIN.</summary>
public sealed record CustomerPinSetRequest(string Pin);

/// <summary>
/// Unlock with a PIN instead of a fresh OTP.
/// </summary>
/// <param name="Identifier">The customer's phone (E.164) or email address.</param>
public sealed record CustomerPinVerifyRequest(string Identifier, string Pin, string? BrandCode = null);

// ─── Customer auth responses ────────────────────────────────────────────────

/// <param name="IsNewCustomer">True when this call created the customer record.</param>
/// <param name="NeedsPhone">
/// True when the account has no verified phone number yet — the app should offer (but not
/// force) the phone-link step. Google sign-up deliberately allows skipping it.
/// </param>
/// <param name="HasPin">
/// True when the customer has set an unlock PIN, so the app can offer PIN/biometric unlock
/// on the next launch instead of another OTP round-trip.
/// </param>
/// <param name="ProfileComplete">
/// False while any of name / email / phone is missing — drives the dashboard
/// "complete your profile" ribbon. Never blocks access to the dashboard.
/// </param>
public sealed record CustomerTokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string TokenType = "Bearer",
    bool IsNewCustomer = false,
    bool NeedsPhone = false,
    bool HasPin = false,
    bool ProfileComplete = false
);

/// <summary>
/// Which profile fields are still blank. Purely advisory — nothing here gates the dashboard;
/// order placement collects what it needs at order time.
/// </summary>
/// <param name="MissingFields">
/// Machine-readable field keys the client maps to localized labels: name, email, phone.
/// Address is deliberately absent — it is collected at order time, where it is actually
/// needed, rather than nagged for on the dashboard.
/// </param>
public sealed record ProfileCompletionDto(
    bool IsComplete,
    int PercentComplete,
    IReadOnlyList<string> MissingFields
);

public sealed record CustomerMeResponse(
    Guid CustomerId,
    Guid BrandId,
    string? Phone,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string Status,
    string? Email = null,
    string? AvatarUrl = null,
    bool PhoneVerified = false,
    bool EmailVerified = false,
    bool HasPin = false,
    IReadOnlyList<string>? LinkedProviders = null,
    ProfileCompletionDto? ProfileCompletion = null
);

/// <summary>Result of attaching a phone number to an existing account.</summary>
public sealed record CustomerPhoneLinkedResponse(
    string Phone,
    bool ProfileComplete
);
