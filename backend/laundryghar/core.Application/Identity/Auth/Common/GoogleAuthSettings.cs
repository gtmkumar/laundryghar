namespace core.Application.Identity.Auth.Common;

/// <summary>
/// OAuth client IDs issued by Google Cloud for this project, used as the accepted
/// <c>aud</c> values when verifying a Google ID token.
///
/// Every platform that signs a user in gets its own client ID, and the token Google
/// returns carries THAT platform's ID in <c>aud</c> — so all of them must be listed
/// here or logins from that platform fail with an audience mismatch.
///
/// Configuration (appsettings / env, section "GoogleAuth"):
///   GoogleAuth__WebClientId     — GCP "Web application" client. Also used by
///                                 admin-web, pos-web, and Expo web.
///   GoogleAuth__AndroidClientId — GCP "Android" client (bound to package name + SHA-1).
///   GoogleAuth__IosClientId     — GCP "iOS" client (bound to the bundle identifier).
///
/// None of these are secrets — they ship inside the client apps. The corresponding
/// client SECRET is never needed here: this service only verifies ID tokens, it does
/// not exchange authorization codes.
/// </summary>
public sealed class GoogleAuthSettings
{
    public const string SectionName = "GoogleAuth";

    public string? WebClientId { get; set; }
    public string? AndroidClientId { get; set; }
    public string? IosClientId { get; set; }

    /// <summary>
    /// Extra accepted audiences beyond the three platform clients — e.g. a second web
    /// client used by a staging origin. Configured as GoogleAuth__AdditionalClientIds__0, __1, …
    /// </summary>
    public string[] AdditionalClientIds { get; set; } = [];

    /// <summary>
    /// Every non-empty client ID, de-duplicated. A token whose <c>aud</c> is not in this
    /// set is rejected. Empty means Google sign-in is not configured and the endpoints
    /// fail closed.
    /// </summary>
    public IReadOnlyCollection<string> AllowedAudiences() =>
        new[] { WebClientId, AndroidClientId, IosClientId }
            .Concat(AdditionalClientIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public bool IsConfigured => AllowedAudiences().Count > 0;
}
