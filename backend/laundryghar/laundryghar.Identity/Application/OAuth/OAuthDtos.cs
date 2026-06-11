namespace laundryghar.Identity.Application.OAuth;

// ── RFC 7591 Dynamic Client Registration ────────────────────────────────────

/// <param name="ClientName">Human-readable name for the client application.</param>
/// <param name="RedirectUris">
/// Allowed redirect URIs. Validated: https:// (any host exact-match) or
/// http://localhost / http://127.0.0.1 (port-agnostic loopback). All other http:// rejected.
/// </param>
public sealed record OAuthRegisterRequest(
    string ClientName,
    string[] RedirectUris
);

public sealed record OAuthRegisterResponse(
    string ClientId,
    string ClientName,
    string[] RedirectUris
);

// ── /oauth/authorize backing endpoints ──────────────────────────────────────

/// <param name="Phone">E.164 phone number of the customer.</param>
public sealed record OAuthOtpSendRequest(string Phone);

/// <summary>
/// Submitted by the authorize page's JavaScript after the user enters their OTP.
/// Contains all OAuth flow parameters needed to create the authorization code.
/// </summary>
public sealed record OAuthApproveRequest(
    string Phone,
    string Code,
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string? State,
    string? Scope
);

public sealed record OAuthApproveResponse(string RedirectUrl);

// ── RFC 6749 Token Response ──────────────────────────────────────────────────

/// <summary>
/// OAuth 2.1 token response (RFC 6749 §5.1).
/// Emitted by /oauth/token for both authorization_code and refresh_token grants.
/// </summary>
public sealed record OAuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string RefreshToken,
    string Scope
);
