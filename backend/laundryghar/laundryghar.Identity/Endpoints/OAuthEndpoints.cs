using laundryghar.Identity.Application.Auth.Commands;
using laundryghar.Identity.Application.Auth.Dtos;
using laundryghar.Identity.Application.OAuth;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using MediatR;
using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// OAuth 2.1 authorization-server facade endpoints.
/// Provides the MCP discovery handshake so Claude.ai, Claude Code, and Gemini CLI can
/// authenticate LaundryGhar customers without duplicating OTP logic.
///
/// Endpoints registered here (all .AllowAnonymous() + "auth" rate-limit policy):
///   GET  /.well-known/oauth-authorization-server  — RFC 8414 server metadata
///   POST /oauth/register                           — RFC 7591 dynamic client registration
///   GET  /oauth/authorize                          — consent page (HTML/JS)
///   POST /oauth/authorize/otp/send                 — backing: OTP send for the authorize flow
///   POST /oauth/authorize/approve                  — backing: OTP verify → issue auth code
///   POST /oauth/token                              — token exchange (authorization_code + refresh_token)
///
/// Security invariants:
///   PKCE-S256: code_challenge stored hashed; code_verifier verified at /oauth/token (RFC 7636).
///   Single-use: codes marked consumed atomically; ConsumedAt non-null = rejected immediately.
///   Redirect-URI: https exact-match; http://localhost|127.0.0.1 port-agnostic (RFC 8252 §8.3).
///   No client_secret: public clients only (OAuth 2.1 mandates PKCE instead).
/// </summary>
public static class OAuthEndpoints
{
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // ── RFC 8414 — Authorization Server Metadata ─────────────────────────
        // Served at well-known location; no rate-limit needed (GET, cacheable).
        app.MapGet("/.well-known/oauth-authorization-server",
            (HttpContext ctx, IOptions<JwtSettings> jwt) =>
            {
                var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                return Results.Json(new
                {
                    issuer = jwt.Value.Issuer,
                    authorization_endpoint = $"{baseUrl}/oauth/authorize",
                    token_endpoint = $"{baseUrl}/oauth/token",
                    registration_endpoint = $"{baseUrl}/oauth/register",
                    response_types_supported = new[] { "code" },
                    grant_types_supported = new[] { "authorization_code", "refresh_token" },
                    code_challenge_methods_supported = new[] { "S256" },
                    token_endpoint_auth_methods_supported = new[] { "none" },
                    scopes_supported = new[] { "mcp:booking" }
                });
            })
        .AllowAnonymous()
        .WithTags("OAuth");

        // ── RFC 7591 — Dynamic Client Registration ────────────────────────────
        app.MapPost("/oauth/register",
            async (OAuthRegisterRequest req, LaundryGharDbContext db, CancellationToken ct) =>
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(req.ClientName))
                    return Results.BadRequest(OAuthError("invalid_client_metadata", "client_name is required."));

                if (req.RedirectUris is null || req.RedirectUris.Length == 0)
                    return Results.BadRequest(OAuthError("invalid_redirect_uri", "At least one redirect_uri is required."));

                // Validate each redirect URI
                foreach (var uri in req.RedirectUris)
                {
                    if (!PkceHelper.IsValidRedirectUri(uri))
                        return Results.BadRequest(OAuthError("invalid_redirect_uri",
                            $"'{uri}' is not an acceptable redirect URI. " +
                            "Only https:// (any host) or http://localhost / http://127.0.0.1 (any port) are permitted."));
                }

                var clientId = PkceHelper.GenerateClientId();
                var now = DateTimeOffset.UtcNow;

                var client = new OAuthClient
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    ClientName = req.ClientName.Trim(),
                    RedirectUris = req.RedirectUris.Select(u => u.Trim()).ToArray(),
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                db.OAuthClients.Add(client);
                await db.SaveChangesAsync(ct);

                return Results.Ok(new OAuthRegisterResponse(
                    ClientId: clientId,
                    ClientName: client.ClientName,
                    RedirectUris: client.RedirectUris));
            })
        .AllowAnonymous()
        .RequireRateLimiting("oauth_register")   // 3/hour per IP — tighter than the shared "auth" policy
        .WithTags("OAuth");

        // ── GET /oauth/authorize — serves the consent/OTP page ───────────────
        // The page handles all UI state (phone entry → OTP → consent → redirect)
        // via two POST backing endpoints below. No server-side session needed.
        app.MapGet("/oauth/authorize",
            (HttpContext ctx,
             LaundryGharDbContext db,
             CancellationToken ct) =>
            {
                var q = ctx.Request.Query;

                string? responseType = q["response_type"];
                string? clientId = q["client_id"];
                string? redirectUri = q["redirect_uri"];
                string? codeChallenge = q["code_challenge"];
                string? codeChallengeMethod = q["code_challenge_method"];
                string? state = q["state"];
                string? scope = q["scope"];

                // Basic parameter validation — return error as query-string redirect
                // if redirect_uri is already known-bad; otherwise embed error in page.
                if (string.IsNullOrWhiteSpace(responseType) || responseType != "code")
                    return Results.BadRequest(OAuthError("invalid_request", "response_type must be 'code'."));

                if (string.IsNullOrWhiteSpace(clientId))
                    return Results.BadRequest(OAuthError("invalid_request", "client_id is required."));

                if (string.IsNullOrWhiteSpace(redirectUri))
                    return Results.BadRequest(OAuthError("invalid_request", "redirect_uri is required."));

                if (string.IsNullOrWhiteSpace(codeChallenge))
                    return Results.BadRequest(OAuthError("invalid_request", "code_challenge is required."));

                if (string.IsNullOrWhiteSpace(codeChallengeMethod) || codeChallengeMethod != "S256")
                    return Results.BadRequest(OAuthError("invalid_request", "code_challenge_method must be 'S256'."));

                // Serve the inline HTML page — validation of client_id + redirect_uri
                // happens at /oauth/authorize/approve (where we have async DB access).
                // This keeps the GET handler synchronous and fast.
                var html = BuildAuthorizePage(clientId!, redirectUri!, codeChallenge!, state, scope);

                // [Fix 4] Strict CSP + framing protection for this HTML response.
                // All inline styles/scripts are covered by unsafe-inline; no external resources.
                ctx.Response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
                ctx.Response.Headers["X-Frame-Options"] = "DENY";
                return Results.Content(html, "text/html; charset=utf-8");
            })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .WithTags("OAuth");

        // ── POST /oauth/authorize/otp/send ────────────────────────────────────
        // Reuses existing CustomerOtpSendCommand + rate-limiting/cooldown logic.
        // Brand resolved from DefaultBrandCode config (same as the public OTP endpoint
        // with no X-Brand-Id header and no brandCode body field).
        app.MapPost("/oauth/authorize/otp/send",
            async (OAuthOtpSendRequest req,
                   HttpContext ctx,
                   ISender sender,
                   IConfiguration config,
                   LaundryGharDbContext db,
                   CancellationToken ct) =>
            {
                var brandId = await ResolveDefaultBrandIdAsync(db, config, ct);
                var ip = ctx.Connection.RemoteIpAddress?.ToString();
                var ua = ctx.Request.Headers.UserAgent.ToString();

                var result = await sender.Send(
                    new CustomerOtpSendCommand(req.Phone, brandId, ip, ua), ct);

                return Results.Ok(new SingleResponse<OtpSentResponse>
                {
                    Status = true,
                    Data = result
                });
            })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .WithTags("OAuth");

        // ── POST /oauth/authorize/approve ─────────────────────────────────────
        // Verifies OTP (via existing handler), resolves/creates customer, validates
        // client + redirect_uri, then issues a single-use PKCE authorization code.
        app.MapPost("/oauth/authorize/approve",
            async (OAuthApproveRequest req,
                   HttpContext ctx,
                   ISender sender,
                   IConfiguration config,
                   LaundryGharDbContext db,
                   ILogger<Program> logger,
                   CancellationToken ct) =>
            {
                // 1. Validate required parameters
                if (string.IsNullOrWhiteSpace(req.Phone)
                 || string.IsNullOrWhiteSpace(req.Code)
                 || string.IsNullOrWhiteSpace(req.ClientId)
                 || string.IsNullOrWhiteSpace(req.RedirectUri)
                 || string.IsNullOrWhiteSpace(req.CodeChallenge))
                {
                    return Results.BadRequest(OAuthError("invalid_request", "Missing required parameters."));
                }

                // 2. Validate client and redirect_uri
                var client = await db.OAuthClients
                    .Where(c => c.ClientId == req.ClientId && c.IsActive)
                    .FirstOrDefaultAsync(ct);

                if (client is null)
                    return Results.BadRequest(OAuthError("invalid_client", "Unknown client_id."));

                // Find matching redirect_uri using port-agnostic loopback comparison
                var matchedRedirect = client.RedirectUris
                    .FirstOrDefault(r => PkceHelper.RedirectUriMatches(r, req.RedirectUri));

                if (matchedRedirect is null)
                    return Results.BadRequest(OAuthError("invalid_redirect_uri",
                        "redirect_uri does not match any registered URI for this client."));

                // 3. Resolve brand (same default logic as the public OTP endpoints)
                Guid brandId;
                try
                {
                    brandId = await ResolveDefaultBrandIdAsync(db, config, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "OAuth authorize/approve: failed to resolve default brand.");
                    return Results.Problem(
                        detail: "Brand configuration error.",
                        statusCode: 500);
                }

                // 4. Verify OTP via existing handler (inherits lockout / salted-hash / rate-limit)
                var ip = ctx.Connection.RemoteIpAddress?.ToString();
                var ua = ctx.Request.Headers.UserAgent.ToString();

                // Verify OTP — result discarded; we issue our own auth code below.
                // The verify call handles lockout / salted-hash / attempt counting.
                try
                {
                    await sender.Send(
                        new CustomerOtpVerifyCommand(req.Phone, req.Code, brandId, ip, ua), ct);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Results.Json(
                        OAuthError("access_denied", ex.Message),
                        statusCode: 401);
                }
                catch (laundryghar.Utilities.Exceptions.BusinessRuleException ex)
                {
                    return Results.Json(
                        OAuthError("access_denied", ex.Message),
                        statusCode: 429);
                }

                // 5. Resolve the customer row just verified (needed for IDs)
                var customer = await db.Customers
                    .Where(c => c.BrandId == brandId && c.PhoneE164 == req.Phone)
                    .Select(c => new { c.Id, c.BrandId })
                    .FirstOrDefaultAsync(ct);

                if (customer is null)
                    return Results.Problem(detail: "Customer not found after OTP verify.", statusCode: 500);

                // 6. Create single-use authorization code (≥ 256-bit random, stored hashed)
                var rawCode = PkceHelper.GenerateRawCode();
                var codeHash = PkceHelper.HashCode(rawCode);
                var now = DateTimeOffset.UtcNow;

                db.OAuthAuthorizationCodes.Add(new OAuthAuthorizationCode
                {
                    Id = Guid.NewGuid(),
                    CodeHash = codeHash,
                    ClientId = client.ClientId,
                    RedirectUri = req.RedirectUri,      // exact URI presented
                    CodeChallenge = req.CodeChallenge,
                    CustomerId = customer.Id,
                    BrandId = customer.BrandId,
                    Scope = "mcp:booking",
                    ExpiresAt = now.AddMinutes(5),
                    ConsumedAt = null,
                    CreatedAt = now
                });

                await db.SaveChangesAsync(ct);

                // 7. Build the redirect URL: redirect_uri?code=...&state=...
                var redirectUrl = BuildRedirectUrl(req.RedirectUri, rawCode, req.State);

                return Results.Ok(new OAuthApproveResponse(
                    RedirectUrl: redirectUrl));
            })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .WithTags("OAuth");

        // ── POST /oauth/token ─────────────────────────────────────────────────
        // Form-encoded per OAuth spec (application/x-www-form-urlencoded).
        app.MapPost("/oauth/token",
            async (HttpContext ctx,
                   LaundryGharDbContext db,
                   IJwtTokenService jwt,
                   IOptions<JwtSettings> jwtOptions,
                   ILogger<Program> logger,
                   CancellationToken ct) =>
            {
                // OAuth spec requires form-encoded body
                if (!ctx.Request.HasFormContentType)
                    return Results.BadRequest(OAuthError("invalid_request",
                        "Content-Type must be application/x-www-form-urlencoded."));

                IFormCollection form;
                try { form = await ctx.Request.ReadFormAsync(ct); }
                catch { return Results.BadRequest(OAuthError("invalid_request", "Could not read form body.")); }

                var grantType = form["grant_type"].ToString();

                if (grantType == "authorization_code")
                    return await HandleAuthorizationCodeGrantAsync(form, db, jwt, jwtOptions, logger, ctx, ct);

                if (grantType == "refresh_token")
                    return await HandleRefreshTokenGrantAsync(form, db, jwt, jwtOptions, logger, ctx, ct);

                return Results.BadRequest(OAuthError("unsupported_grant_type",
                    $"grant_type '{grantType}' is not supported."));
            })
        .AllowAnonymous()
        .RequireRateLimiting("auth")
        .WithTags("OAuth");

        return app;
    }

    // ── authorization_code grant ─────────────────────────────────────────────

    private static async Task<IResult> HandleAuthorizationCodeGrantAsync(
        IFormCollection form,
        LaundryGharDbContext db,
        IJwtTokenService jwt,
        IOptions<JwtSettings> jwtOptions,
        ILogger logger,
        HttpContext ctx,
        CancellationToken ct)
    {
        var code = form["code"].ToString();
        var clientId = form["client_id"].ToString();
        var redirectUri = form["redirect_uri"].ToString();
        var codeVerifier = form["code_verifier"].ToString();

        if (string.IsNullOrWhiteSpace(code)
         || string.IsNullOrWhiteSpace(clientId)
         || string.IsNullOrWhiteSpace(redirectUri)
         || string.IsNullOrWhiteSpace(codeVerifier))
        {
            return Results.BadRequest(OAuthError("invalid_request",
                "code, client_id, redirect_uri, and code_verifier are all required."));
        }

        // [RFC 7636] Reject code_verifier outside the valid 43–128 character range.
        if (codeVerifier.Length < 43 || codeVerifier.Length > 128)
        {
            return Results.BadRequest(OAuthError("invalid_request",
                "code_verifier must be between 43 and 128 characters (RFC 7636 §4.1)."));
        }

        var codeHash = PkceHelper.HashCode(code);

        // Atomically mark the code as consumed: UPDATE … SET consumed_at = now()
        // WHERE code_hash = … AND consumed_at IS NULL AND expires_at > now()
        // Returns 0 rows on replay (already consumed) or expiry — no TOCTOU window.
        var now = DateTimeOffset.UtcNow;
        var affected = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE identity_access.oauth_authorization_codes
               SET consumed_at = {now}
             WHERE code_hash   = {codeHash}
               AND consumed_at IS NULL
               AND expires_at  > {now}
            """,
            ct);

        if (affected == 0)
        {
            // Check if it was expired vs replayed for logging purposes (no user-visible difference)
            var exists = await db.OAuthAuthorizationCodes
                .AnyAsync(c => c.CodeHash == codeHash, ct);

            if (exists)
                logger.LogWarning("OAuth token exchange: authorization code replay detected for client {ClientId}.", clientId);

            return Results.Json(OAuthError("invalid_grant", "Authorization code is invalid, expired, or already used."),
                statusCode: 400);
        }

        // Load the now-consumed row to validate the remaining parameters
        var authCode = await db.OAuthAuthorizationCodes
            .Where(c => c.CodeHash == codeHash)
            .FirstOrDefaultAsync(ct);

        if (authCode is null)
            return Results.Json(OAuthError("invalid_grant", "Authorization code not found."), statusCode: 400);

        // Validate client_id + redirect_uri match what was stored.
        // [M2] All mismatch/inactive failures return a single generic message — granular reason in server log only.
        if (authCode.ClientId != clientId)
        {
            logger.LogWarning("OAuth token exchange: client_id mismatch — stored {Stored}, presented {Presented}.",
                authCode.ClientId, clientId);
            return Results.Json(OAuthError("invalid_grant", "Authorization grant is invalid."), statusCode: 400);
        }

        if (!PkceHelper.RedirectUriMatches(authCode.RedirectUri, redirectUri))
        {
            logger.LogWarning("OAuth token exchange: redirect_uri mismatch — stored {Stored}, presented {Presented}.",
                authCode.RedirectUri, redirectUri);
            return Results.Json(OAuthError("invalid_grant", "Authorization grant is invalid."), statusCode: 400);
        }

        // PKCE: verify code_verifier against stored code_challenge (S256)
        if (!PkceHelper.VerifyCodeVerifier(codeVerifier, authCode.CodeChallenge))
        {
            logger.LogWarning("OAuth token exchange: code_verifier does not match code_challenge for client {ClientId}.",
                clientId);
            return Results.Json(OAuthError("invalid_grant", "Authorization grant is invalid."), statusCode: 400);
        }

        // Look up the customer (must still be active)
        var customer = await db.Customers.FindAsync([authCode.CustomerId], ct);
        if (customer is null || customer.Status != "active")
        {
            logger.LogWarning("OAuth token exchange: customer {CustomerId} not found or not active.", authCode.CustomerId);
            return Results.Json(OAuthError("invalid_grant", "Authorization grant is invalid."), statusCode: 400);
        }

        // Issue tokens — OAuth path: token_use=customer_mcp + scope claim.
        var jwtSettings = jwtOptions.Value;

        var accessToken = jwt.CreateOAuthCustomerAccessToken(
            new CustomerTokenClaims(
                CustomerId: customer.Id,
                BrandId: customer.BrandId,
                Phone: customer.PhoneE164),
            authCode.Scope);
        var rawRefresh = jwt.GenerateRefreshTokenRaw();
        var tokenHash = jwt.HashRefreshToken(rawRefresh);
        var rtId = Guid.NewGuid();

        // RemoteIpAddress is already an IPAddress — use directly
        var ipAddress = ctx.Connection.RemoteIpAddress;

        var refreshToken = new RefreshToken
        {
            Id = rtId,
            CustomerId = customer.Id,
            TokenHash = tokenHash,
            FamilyId = rtId,
            IpAddress = ipAddress,
            UserAgent = ctx.Request.Headers.UserAgent.ToString(),
            IssuedAt = now,
            ExpiresAt = now.AddDays(jwtSettings.RefreshDays),
            CreatedAt = now
        };

        await laundryghar.Identity.Infrastructure.Services.RefreshTokenRepository.InsertRootAsync(
            db, refreshToken, ct);

        // [Fix 2b] Mark client as recently used — enables cleanup of never-used abandoned registrations.
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE identity_access.oauth_clients
               SET last_used_at = {now}
             WHERE client_id = {authCode.ClientId}
            """,
            ct);

        return Results.Ok(new OAuthTokenResponse(
            AccessToken: accessToken,
            TokenType: "Bearer",
            ExpiresIn: jwtSettings.AccessMinutes * 60,
            RefreshToken: rawRefresh,
            Scope: authCode.Scope));
    }

    // ── refresh_token grant ──────────────────────────────────────────────────
    // Handled inline (not via CustomerRefreshCommand) so that the rotated access token
    // carries token_use=customer_mcp + scope=mcp:booking rather than token_use=customer.
    // The rotation logic mirrors CustomerRefreshHandler exactly.

    private static async Task<IResult> HandleRefreshTokenGrantAsync(
        IFormCollection form,
        LaundryGharDbContext db,
        IJwtTokenService jwt,
        IOptions<JwtSettings> jwtOptions,
        ILogger logger,
        HttpContext ctx,
        CancellationToken ct)
    {
        var rawRefreshToken = form["refresh_token"].ToString();
        var clientId = form["client_id"].ToString();

        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return Results.BadRequest(OAuthError("invalid_request", "refresh_token is required."));

        var tokenHash = jwt.HashRefreshToken(rawRefreshToken);

        var existing = await db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
            return Results.Json(OAuthError("invalid_grant", "Invalid refresh token."), statusCode: 400);

        // Must be a customer token (customer_id set, user_id null)
        if (!existing.CustomerId.HasValue || existing.UserId.HasValue)
            return Results.Json(OAuthError("invalid_grant", "Invalid refresh token."), statusCode: 400);

        if (existing.RevokedAt.HasValue)
        {
            // Reuse detected — revoke entire family
            var family = await db.RefreshTokens
                .Where(t => t.FamilyId == existing.FamilyId && t.RevokedAt == null)
                .ToListAsync(ct);
            family.ForEach(t => { t.RevokedAt = DateTimeOffset.UtcNow; t.RevokedReason = "reuse_detected"; });
            await db.SaveChangesAsync(ct);
            logger.LogWarning("OAuth refresh: reuse detected for family {FamilyId}.", existing.FamilyId);
            return Results.Json(OAuthError("invalid_grant", "Refresh token reuse detected. Please log in again."), statusCode: 400);
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
            return Results.Json(OAuthError("invalid_grant", "Refresh token expired."), statusCode: 400);

        var customer = await db.Customers.FindAsync([existing.CustomerId.Value], ct);
        if (customer is null || customer.Status != "active")
            return Results.Json(OAuthError("invalid_grant", "Customer account is not active."), statusCode: 400);

        var ipAddress = ctx.Connection.RemoteIpAddress;
        var ua = ctx.Request.Headers.UserAgent.ToString();
        var jwtSettings = jwtOptions.Value;
        var now = DateTimeOffset.UtcNow;

        // Rotate: mark existing token revoked, issue new token pair
        existing.RevokedAt = now;
        existing.RevokedReason = "rotated";

        // Issue access token with token_use=customer_mcp + scope=mcp:booking
        var accessToken = jwt.CreateOAuthCustomerAccessToken(
            new CustomerTokenClaims(
                CustomerId: customer.Id,
                BrandId: customer.BrandId,
                Phone: customer.PhoneE164),
            "mcp:booking");

        var rawNewRefresh = jwt.GenerateRefreshTokenRaw();
        var newTokenHash = jwt.HashRefreshToken(rawNewRefresh);

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            TokenHash = newTokenHash,
            FamilyId = existing.FamilyId,
            ParentTokenId = existing.Id,
            IpAddress = ipAddress,
            UserAgent = ua,
            IssuedAt = now,
            ExpiresAt = now.AddDays(jwtSettings.RefreshDays),
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);

        return Results.Ok(new OAuthTokenResponse(
            AccessToken: accessToken,
            TokenType: "Bearer",
            ExpiresIn: jwtSettings.AccessMinutes * 60,
            RefreshToken: rawNewRefresh,
            Scope: "mcp:booking"));
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the default brand ID using DefaultBrandCode config key.
    /// Matches the logic in CustomerAuthEndpoints.ResolveBrandIdAsync when no header/body brand is provided.
    /// </summary>
    private static async Task<Guid> ResolveDefaultBrandIdAsync(
        LaundryGharDbContext db,
        IConfiguration config,
        CancellationToken ct)
    {
        var code = config["CustomerAuth:DefaultBrandCode"] ?? "LG-MAIN";
        var brand = await db.Brands
            .Where(b => b.Code == code)
            .Select(b => new { b.Id })
            .FirstOrDefaultAsync(ct);

        if (brand is null)
            throw new laundryghar.Utilities.Exceptions.ValidationException(
                new Dictionary<string, string[]>
                {
                    ["brandCode"] = [$"Default brand '{code}' not found."]
                });

        return brand.Id;
    }

    /// <summary>RFC 6749 §5.2 error response object.</summary>
    private static object OAuthError(string error, string? description = null)
        => description is null
            ? new { error }
            : new { error, error_description = description };

    // ── HTML authorize page (self-contained, no SPA build) ───────────────────

    /// <summary>Builds the redirect URL appending code and optionally state as query params.</summary>
    private static string BuildRedirectUrl(string redirectUri, string code, string? state)
    {
        // Manual query-string append — avoids System.Web dependency
        var separator = redirectUri.Contains('?') ? '&' : '?';
        var url = $"{redirectUri}{separator}code={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(state))
            url += $"&state={Uri.EscapeDataString(state)}";
        return url;
    }

    private static string BuildAuthorizePage(
        string clientId,
        string redirectUri,
        string codeChallenge,
        string? state,
        string? scope)
    {
        // Parameters are JSON-serialized using System.Text.Json with JavaScriptEncoder.Default.
        // JavaScriptEncoder.Default escapes <, >, &, ', and " as <, >, &, ', " —
        // making the values safe when embedded as JS string literals inside an inline <script> block.
        // Do NOT switch to HtmlEncoder here: HTML-entity encoding is incorrect for JS string contexts.
        var safeClientId = clientId;
        var safeRedirectUri = redirectUri;
        var safeCodeChallenge = codeChallenge;
        var safeState = state ?? "";
        var safeScope = scope ?? "mcp:booking";

        // Single-page HTML + vanilla JS. No external resources.
        // The JS calls two API endpoints on this same origin:
        //   POST /oauth/authorize/otp/send   — sends the OTP
        //   POST /oauth/authorize/approve    — verifies OTP + issues code → navigates to redirect_uri
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>Sign in to LaundryGhar</title>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: #f5f5f0;
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
    }
    .card {
      background: #fff;
      border-radius: 12px;
      padding: 2rem;
      width: 100%;
      max-width: 400px;
      box-shadow: 0 4px 24px rgba(0,0,0,0.10);
    }
    h1 { font-size: 1.4rem; font-weight: 700; color: #1a1a1a; margin-bottom: 0.25rem; }
    .subtitle { color: #666; font-size: 0.9rem; margin-bottom: 1.5rem; }
    .consent-box {
      background: #f9f6f0;
      border-left: 3px solid #c9a84c;
      padding: 0.75rem 1rem;
      border-radius: 4px;
      font-size: 0.85rem;
      color: #555;
      margin-bottom: 1.5rem;
    }
    label { display: block; font-size: 0.85rem; font-weight: 600; color: #333; margin-bottom: 0.3rem; }
    input[type=tel], input[type=text] {
      width: 100%;
      padding: 0.65rem 0.9rem;
      border: 1px solid #ddd;
      border-radius: 8px;
      font-size: 1rem;
      margin-bottom: 0.75rem;
      outline: none;
      transition: border-color 0.2s;
    }
    input:focus { border-color: #c9a84c; }
    button {
      width: 100%;
      padding: 0.75rem;
      background: #c9a84c;
      color: #fff;
      border: none;
      border-radius: 8px;
      font-size: 1rem;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.2s;
    }
    button:hover:not(:disabled) { background: #b8973b; }
    button:disabled { opacity: 0.6; cursor: not-allowed; }
    .msg { font-size: 0.85rem; margin-top: 0.5rem; min-height: 1.2em; }
    .msg.error { color: #c0392b; }
    .msg.ok    { color: #27ae60; }
    #step-otp  { display: none; }
  </style>
</head>
<body>
  <div class="card">
    <h1>LaundryGhar</h1>
    <p class="subtitle">Sign in to continue</p>

    <div class="consent-box" id="consent-text">
      Allow <strong id="client-display">an app</strong> to book and view your LaundryGhar orders.
    </div>

    <!-- Step 1: Phone number entry -->
    <div id="step-phone">
      <label for="phone">Mobile number (with country code)</label>
      <input type="tel" id="phone" placeholder="+919876543210" autocomplete="tel" />
      <button id="btn-send">Send OTP</button>
      <div class="msg" id="msg-phone"></div>
    </div>

    <!-- Step 2: OTP entry -->
    <div id="step-otp">
      <label for="otp">6-digit OTP</label>
      <input type="text" id="otp" placeholder="123456" maxlength="6" inputmode="numeric" autocomplete="one-time-code" />
      <button id="btn-verify">Verify &amp; Allow</button>
      <div class="msg" id="msg-otp"></div>
    </div>
  </div>

  <script>
    (function () {
      // Parameters JSON-encoded server-side via JavaScriptEncoder.Default (safe for JS string literals)
      const CLIENT_ID       = {{System.Text.Json.JsonSerializer.Serialize(safeClientId)}};
      const REDIRECT_URI    = {{System.Text.Json.JsonSerializer.Serialize(safeRedirectUri)}};
      const CODE_CHALLENGE  = {{System.Text.Json.JsonSerializer.Serialize(safeCodeChallenge)}};
      const STATE           = {{System.Text.Json.JsonSerializer.Serialize(safeState)}};
      const SCOPE           = {{System.Text.Json.JsonSerializer.Serialize(safeScope)}};

      // Fetch client_name for the consent line
      fetch('/oauth/register', { method: 'HEAD' }); // warm connection only
      // We don't have a client-info endpoint; show client_id truncated in consent line
      document.getElementById('client-display').textContent =
        CLIENT_ID.length > 20 ? CLIENT_ID.slice(0, 20) + '…' : CLIENT_ID;

      const phoneInput  = document.getElementById('phone');
      const otpInput    = document.getElementById('otp');
      const btnSend     = document.getElementById('btn-send');
      const btnVerify   = document.getElementById('btn-verify');
      const msgPhone    = document.getElementById('msg-phone');
      const msgOtp      = document.getElementById('msg-otp');
      const stepPhone   = document.getElementById('step-phone');
      const stepOtp     = document.getElementById('step-otp');

      function setMsg(el, text, isError) {
        el.textContent = text;
        el.className   = 'msg ' + (isError ? 'error' : 'ok');
      }

      // ── Step 1: Send OTP ──────────────────────────────────────────────────
      btnSend.addEventListener('click', async () => {
        const phone = phoneInput.value.trim();
        if (!phone) { setMsg(msgPhone, 'Please enter your phone number.', true); return; }

        btnSend.disabled = true;
        setMsg(msgPhone, 'Sending OTP…', false);

        try {
          const res  = await fetch('/oauth/authorize/otp/send', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({ phone })
          });
          const body = await res.json();

          if (res.ok) {
            setMsg(msgPhone, 'OTP sent! Check your SMS.', false);
            stepPhone.style.display = 'none';
            stepOtp.style.display   = 'block';
            otpInput.focus();
          } else {
            const detail = body?.error_description
              || body?.message
              || (body?.errors && Object.values(body.errors).flat().join(' '))
              || 'Failed to send OTP.';
            setMsg(msgPhone, detail, true);
            btnSend.disabled = false;
          }
        } catch {
          setMsg(msgPhone, 'Network error. Please try again.', true);
          btnSend.disabled = false;
        }
      });

      // ── Step 2: Verify OTP + approve ─────────────────────────────────────
      btnVerify.addEventListener('click', async () => {
        const phone = phoneInput.value.trim();
        const code  = otpInput.value.trim();
        if (!code || code.length !== 6) {
          setMsg(msgOtp, 'Please enter the 6-digit OTP.', true); return;
        }

        btnVerify.disabled = true;
        setMsg(msgOtp, 'Verifying…', false);

        try {
          const res  = await fetch('/oauth/authorize/approve', {
            method:  'POST',
            headers: { 'Content-Type': 'application/json' },
            body:    JSON.stringify({
              phone,
              code,
              clientId:      CLIENT_ID,
              redirectUri:   REDIRECT_URI,
              codeChallenge: CODE_CHALLENGE,
              state:         STATE,
              scope:         SCOPE
            })
          });
          const body = await res.json();

          if (res.ok && body.redirectUrl) {
            setMsg(msgOtp, 'Verified! Redirecting…', false);
            window.location.href = body.redirectUrl;
          } else {
            const detail = body?.error_description
              || body?.message
              || 'Verification failed.';
            setMsg(msgOtp, detail, true);
            btnVerify.disabled = false;
          }
        } catch {
          setMsg(msgOtp, 'Network error. Please try again.', true);
          btnVerify.disabled = false;
        }
      });

      // Allow Enter key on OTP input
      otpInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') btnVerify.click();
      });
      phoneInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') btnSend.click();
      });
    })();
  </script>
</body>
</html>
""";
    }
}
