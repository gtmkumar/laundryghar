namespace core.Application.Identity.Auth.Dtos;

// ─── RaaS partner auth requests (issue #14) ─────────────────────────────────

/// <param name="Phone">E.164 phone of a registered partner user, e.g. +919876543210.</param>
public sealed record PartnerOtpSendRequest(string Phone);

/// <param name="Phone">E.164 phone the OTP was sent to.</param>
/// <param name="Code">The 6-digit OTP.</param>
public sealed record PartnerOtpVerifyRequest(string Phone, string Code);

// ─── RaaS partner auth response ─────────────────────────────────────────────

/// <summary>Result of a successful partner OTP login: a <c>token_use=partner</c> access token
/// only. No refresh token is issued for the RaaS MVP (re-login on expiry).</summary>
public sealed record PartnerTokenResponse(
    string AccessToken,
    int ExpiresInSeconds,
    string TokenType = "Bearer"
);
