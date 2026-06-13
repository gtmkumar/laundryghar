using laundryghar.Identity.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// OIDC-style discovery + JWKS endpoints. Identity is the sole JWT issuer; every other
/// service verifies RS256 signatures by fetching the public key published here. Anonymous.
/// </summary>
public static class WellKnownEndpoints
{
    public static IEndpointRouteBuilder MapWellKnownEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /.well-known/jwks.json — the RS256 public key set.
        app.MapGet("/.well-known/jwks.json", (IJwtKeyProvider keys) =>
        {
            var jwk = keys.PublicJwk;
            return Results.Json(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = jwk.Kty,
                        use = jwk.Use,
                        kid = jwk.Kid,
                        alg = jwk.Alg,
                        n   = jwk.N,
                        e   = jwk.E,
                    }
                }
            });
        })
        .AllowAnonymous()
        .WithTags("Well-Known");

        // GET /.well-known/openid-configuration — minimal discovery doc so verifying
        // services' JwtBearer ConfigurationManager can locate the JWKS.
        app.MapGet("/.well-known/openid-configuration",
            (HttpContext ctx, IOptions<JwtSettings> jwt) =>
        {
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            return Results.Json(new
            {
                issuer   = jwt.Value.Issuer,
                jwks_uri = $"{baseUrl}/.well-known/jwks.json",
                id_token_signing_alg_values_supported = new[] { "RS256" },
                response_types_supported = new[] { "token" },
                subject_types_supported  = new[] { "public" },
            });
        })
        .AllowAnonymous()
        .WithTags("Well-Known");

        return app;
    }
}
