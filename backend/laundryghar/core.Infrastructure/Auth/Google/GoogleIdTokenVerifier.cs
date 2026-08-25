using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace core.Infrastructure.Auth.Google;

/// <summary>
/// Verifies Google ID tokens against Google's published RS256 signing keys.
///
/// Key material comes from Google's OIDC discovery document via
/// <see cref="ConfigurationManager{T}"/>, which caches the JWKS and refreshes it on its own
/// schedule — Google rotates signing keys roughly daily, so hard-coding or long-caching the
/// key set breaks logins. On a signature failure the manager is asked to refresh once and
/// validation is retried, which covers the window right after a rotation.
///
/// Registered as a singleton: the ConfigurationManager's cache is the whole point, and a
/// scoped registration would refetch the JWKS on every login.
/// </summary>
public sealed class GoogleIdTokenVerifier : IGoogleIdTokenVerifier
{
    private const string DiscoveryUrl = "https://accounts.google.com/.well-known/openid-configuration";

    /// <summary>
    /// Google issues tokens under both spellings and treats them as equivalent.
    /// Both must be accepted or a share of real logins fail.
    /// </summary>
    private static readonly string[] ValidIssuers =
        ["https://accounts.google.com", "accounts.google.com"];

    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configManager;
    private readonly GoogleAuthSettings _settings;
    private readonly ILogger<GoogleIdTokenVerifier> _logger;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public GoogleIdTokenVerifier(
        IConfigurationManager<OpenIdConnectConfiguration> configManager,
        IOptions<GoogleAuthSettings> settings,
        ILogger<GoogleIdTokenVerifier> logger)
    {
        _configManager = configManager;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>Builds the discovery-backed configuration manager. Call from DI registration.</summary>
    public static IConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager() =>
        new ConfigurationManager<OpenIdConnectConfiguration>(
            DiscoveryUrl,
            new OpenIdConnectConfigurationRetriever());

    public async Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new UnauthorizedAccessException("Google sign-in failed: no ID token supplied.");

        // Fail closed. Without configured client IDs any Google token from any project
        // would validate, which would let anyone sign in as anyone.
        var audiences = _settings.AllowedAudiences();
        if (audiences.Count == 0)
        {
            _logger.LogError(
                "Google sign-in attempted but no GoogleAuth client IDs are configured. " +
                "Set GoogleAuth__WebClientId / __AndroidClientId / __IosClientId.");
            throw new UnauthorizedAccessException("Google sign-in is not configured on this server.");
        }

        var config = await _configManager.GetConfigurationAsync(ct);
        var parameters = BuildValidationParameters(audiences, config.SigningKeys);

        ClaimsPrincipal principal;
        try
        {
            principal = ValidateToken(idToken, parameters);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            // Almost always a key rotation: our cached JWKS predates the key that signed
            // this token. Force a refresh and retry once before rejecting the user.
            _logger.LogInformation("Google signing key not found in cached JWKS — refreshing and retrying.");
            _configManager.RequestRefresh();
            var refreshed = await _configManager.GetConfigurationAsync(ct);
            try
            {
                principal = ValidateToken(idToken, BuildValidationParameters(audiences, refreshed.SigningKeys));
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogWarning(ex, "Google ID token rejected after JWKS refresh.");
                throw new UnauthorizedAccessException("Google sign-in failed: the token could not be verified.");
            }
        }
        catch (SecurityTokenException ex)
        {
            // Covers expiry, audience mismatch, issuer mismatch and malformed tokens. The
            // reason is logged but never returned: it would tell an attacker which check failed.
            _logger.LogWarning(ex, "Google ID token rejected.");
            throw new UnauthorizedAccessException("Google sign-in failed: the token could not be verified.");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Malformed Google ID token.");
            throw new UnauthorizedAccessException("Google sign-in failed: the token could not be verified.");
        }

        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
            throw new UnauthorizedAccessException("Google sign-in failed: the token has no subject.");

        return new GoogleIdentity(
            Subject: subject,
            Email: principal.FindFirstValue("email"),
            EmailVerified: ParseBoolClaim(principal.FindFirstValue("email_verified")),
            Name: principal.FindFirstValue("name"),
            GivenName: principal.FindFirstValue("given_name"),
            FamilyName: principal.FindFirstValue("family_name"),
            PictureUrl: principal.FindFirstValue("picture"),
            HostedDomain: principal.FindFirstValue("hd"));
    }

    private ClaimsPrincipal ValidateToken(string idToken, TokenValidationParameters parameters) =>
        _handler.ValidateToken(idToken, parameters, out _);

    private static TokenValidationParameters BuildValidationParameters(
        IReadOnlyCollection<string> audiences,
        ICollection<SecurityKey> signingKeys) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuers = ValidIssuers,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ValidateLifetime = true,
            // Google ID tokens live 1 hour; a small skew absorbs client/server clock drift
            // without meaningfully extending the window an intercepted token stays usable.
            ClockSkew = TimeSpan.FromMinutes(2),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
        };

    /// <summary>
    /// Google serialises <c>email_verified</c> as a JSON boolean, but some clients and older
    /// tokens carry the string "true". Accept both; anything else counts as unverified.
    /// </summary>
    private static bool ParseBoolClaim(string? raw) =>
        bool.TryParse(raw, out var parsed) && parsed;
}
