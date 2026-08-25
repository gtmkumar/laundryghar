using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Auth.ApiKey;
using laundryghar.Utilities.Endpoints;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// The machine-facing API surface (§11 P4). Authenticated by API key only — the
/// <c>apiscope:…</c> policies bind to the <c>ApiKey</c> scheme, so a signed-in human's token does not
/// satisfy them and a key does not satisfy the admin console's permission policies.
///
/// <para>This group is deliberately small. It establishes the mechanism — issuance, scoping,
/// revocation, per-key rate limits, metering, entitlement gating — with one endpoint that proves the
/// whole chain end to end. WHICH business operations to expose publicly, and under what scope names,
/// is a product decision the strategy does not make: §11 P4 says "partner/public API" and §5 sells
/// <c>api_access</c>, neither names an operation. Logged as <b>OQ-12</b>; adding an endpoint here is
/// one line once that is decided.</para>
/// </summary>
public class PublicApi : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/public-api";

    /// <summary>Scope for reading the calling account's own identity. The minimum useful scope, and
    /// the one an integrator uses to check their key works before writing any real code.</summary>
    public const string IdentityScope = "account.read";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Public API");

        // Per-key rate limiting. The policy resolves the caller's own limit from its claim, falling
        // back to the host default — see Program.cs.
        group.MapGet(WhoAmI, "me")
             .RequireAuthorization($"apiscope:{IdentityScope}")
             .RequireRateLimiting("api_key");
    }

    /// <summary>
    /// Identifies the calling key. Returns the brand it belongs to, its environment and its scopes —
    /// everything an integrator needs to confirm the credential is wired up, and nothing that would
    /// be useful to someone who stole it.
    /// </summary>
    public static IResult WhoAmI(HttpContext http, ICurrentTenant tenant)
    {
        var scopes = http.User.FindAll(ApiKeyClaims.ScopeClaim).Select(c => c.Value).ToArray();

        return Results.Ok(new SingleResponse<object>
        {
            Status = true,
            Data = new
            {
                brandId = tenant.BrandId,
                environment = http.User.FindFirst(ApiKeyClaims.EnvironmentClaim)?.Value,
                keyId = http.User.FindFirst(ApiKeyClaims.KeyIdClaim)?.Value,
                scopes,
            },
        });
    }
}
