namespace core.Application.Common.Interfaces;

/// <summary>
/// The subset of a verified Google ID token that LaundryGhar consumes.
/// Only produced after signature, issuer, audience and expiry checks have passed.
/// </summary>
/// <param name="Subject">
/// Google's immutable account identifier (<c>sub</c>). This — not the email — is the
/// join key: a Google account's email address can change, its subject never does.
/// </param>
/// <param name="Email">Primary email on the account. Null when the token carries no email scope.</param>
/// <param name="EmailVerified">
/// Google's own assertion that the user owns this mailbox. Sign-in MUST reject false:
/// an unverified address cannot be trusted to match an existing account by email.
/// </param>
/// <param name="Name">Full display name, when the profile scope was granted.</param>
/// <param name="GivenName">First name.</param>
/// <param name="FamilyName">Last name.</param>
/// <param name="PictureUrl">Avatar URL.</param>
/// <param name="HostedDomain">Google Workspace domain (<c>hd</c>), null for consumer accounts.</param>
public sealed record GoogleIdentity(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? Name,
    string? GivenName,
    string? FamilyName,
    string? PictureUrl,
    string? HostedDomain);

/// <summary>
/// Verifies Google-issued OpenID Connect ID tokens against Google's published signing keys.
/// Implemented in core.Infrastructure over Google's OIDC discovery document, which handles
/// JWKS caching and key rotation.
/// </summary>
public interface IGoogleIdTokenVerifier
{
    /// <summary>
    /// Validates <paramref name="idToken"/> and projects it to a <see cref="GoogleIdentity"/>.
    /// Throws <see cref="UnauthorizedAccessException"/> when the token is malformed, expired,
    /// signed by an unknown key, issued by a different provider, or addressed to an audience
    /// that is not one of this project's configured OAuth client IDs.
    /// </summary>
    Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct = default);
}
