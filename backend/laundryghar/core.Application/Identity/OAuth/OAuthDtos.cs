using System.Text.Json.Serialization;

namespace core.Application.Identity.OAuth;

// ── RFC 7591 Dynamic Client Registration ────────────────────────────────────
// RFC-facing DTOs carry explicit snake_case wire names: OAuth clients
// (Claude, Gemini CLI, any MCP host) send/expect client_name, access_token
// etc., not the camelCase the app-wide JSON options would produce.

/// <param name="ClientName">Human-readable name for the client application.</param>
/// <param name="RedirectUris">
/// Allowed redirect URIs. Validated: https:// (any host exact-match) or
/// http://localhost / http://127.0.0.1 (port-agnostic loopback). All other http:// rejected.
/// </param>
public sealed record OAuthRegisterRequest(
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] string[] RedirectUris
);

public sealed record OAuthRegisterResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("redirect_uris")] string[] RedirectUris
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
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope
);
