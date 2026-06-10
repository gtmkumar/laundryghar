using laundryghar.Identity.Application.Auth.Commands;
using laundryghar.Identity.Application.Auth.Dtos;
using laundryghar.Identity.Infrastructure.Auth;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Endpoints;

/// <summary>
/// POST /api/v1/auth/password/login
/// POST /api/v1/auth/otp/send
/// POST /api/v1/auth/otp/verify
/// POST /api/v1/auth/refresh
/// POST /api/v1/auth/logout
/// POST /api/v1/auth/password/forgot
/// POST /api/v1/auth/password/reset
/// </summary>
public static class AuthEndpoints
{
    // Name of the HttpOnly refresh-token cookie for system users (admin-web).
    private const string RefreshCookieName = "lg_refresh";

    // Path the cookie is scoped to. The browser only sends `lg_refresh` to the
    // refresh endpoint, never to any other route — minimizing exposure surface.
    private const string RefreshCookiePath = "/api/v1/auth/refresh";

    /// <summary>
    /// Writes the refresh token to the HttpOnly `lg_refresh` cookie.
    ///   HttpOnly      — never readable from JS (the XSS fix).
    ///   Secure        — only sent over HTTPS outside Development (http://localhost works in dev).
    ///   SameSite=Strict — not sent on cross-site navigations (CSRF hardening).
    ///   Path          — scoped to the refresh endpoint only.
    ///   Max-Age       — the refresh-token lifetime (Jwt:RefreshDays).
    /// admin-web stops persisting the refresh token in localStorage and relies on
    /// this cookie for silent refresh. The token is ALSO still returned in the body
    /// for pos-web / mobile system users (body wins on refresh for backward compat).
    /// </summary>
    private static void SetRefreshCookie(HttpContext ctx, string refreshToken, int refreshDays, bool isDev)
    {
        ctx.Response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !isDev,                 // dev omits Secure so http://localhost works
            SameSite = SameSiteMode.Strict,
            Path     = RefreshCookiePath,
            MaxAge   = TimeSpan.FromDays(refreshDays),
        });
    }

    /// <summary>Clears the refresh cookie. Path + attributes must match SetRefreshCookie.</summary>
    private static void ClearRefreshCookie(HttpContext ctx, bool isDev)
    {
        ctx.Response.Cookies.Append(RefreshCookieName, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !isDev,
            SameSite = SameSiteMode.Strict,
            Path     = RefreshCookiePath,
            MaxAge   = TimeSpan.Zero,
        });
    }

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        // C5: rate-limit the entire auth group (10 req / 60 s per client IP)
        var auth = group.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");

        // POST /api/v1/auth/password/login
        auth.MapPost("/password/login", async (
            PasswordLoginRequest req,
            HttpContext ctx,
            ISender sender,
            IOptions<JwtSettings> jwt,
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new PasswordLoginCommand(req.Identifier, req.Password, ip, ua), ct);
            // Also set the refresh token as an HttpOnly cookie for browser system users (admin-web).
            // The body still carries it for pos-web / mobile / scripts (backward compat).
            SetRefreshCookie(ctx, result.RefreshToken, jwt.Value.RefreshDays, env.IsDevelopment());
            return Results.Ok(new SingleResponse<TokenResponse> { Status = true, Data = result });
        })
        .WithName("PasswordLogin")
        .Produces<SingleResponse<TokenResponse>>()
        .ProducesProblem(401)
        .AllowAnonymous();

        // POST /api/v1/auth/otp/send
        auth.MapPost("/otp/send", async (
            OtpSendRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new OtpSendCommand(req.Identifier, req.IdentifierType, req.Purpose, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpSentResponse> { Status = true, Data = result });
        })
        .WithName("OtpSend")
        .Produces<SingleResponse<OtpSentResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/otp/verify
        auth.MapPost("/otp/verify", async (
            OtpVerifyRequest req,
            HttpContext ctx,
            ISender sender,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await sender.Send(new OtpVerifyCommand(req.Identifier, req.IdentifierType, req.Purpose, req.Code, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpVerifiedResponse> { Status = true, Data = result });
        })
        .WithName("OtpVerify")
        .Produces<SingleResponse<OtpVerifiedResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/refresh
        auth.MapPost("/refresh", async (
            SystemRefreshTokenRequest req,
            HttpContext ctx,
            ISender sender,
            IOptions<JwtSettings> jwt,
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();

            // Body wins (pos-web / mobile system users); fall back to the HttpOnly
            // cookie (admin-web). If neither is present the command validator rejects
            // the empty token with a 400, which the client treats as a failed refresh.
            var rawRefresh = !string.IsNullOrWhiteSpace(req.RefreshToken)
                ? req.RefreshToken!
                : ctx.Request.Cookies[RefreshCookieName] ?? string.Empty;

            var result = await sender.Send(new RefreshTokenCommand(rawRefresh, ip, ua), ct);
            // Rotate the cookie with the new refresh token (refresh-token rotation).
            SetRefreshCookie(ctx, result.RefreshToken, jwt.Value.RefreshDays, env.IsDevelopment());
            return Results.Ok(new SingleResponse<TokenResponse> { Status = true, Data = result });
        })
        .WithName("RefreshToken")
        .Produces<SingleResponse<TokenResponse>>()
        .AllowAnonymous();

        // POST /api/v1/auth/logout
        auth.MapPost("/logout", async (
            SystemLogoutRequest req,
            HttpContext ctx,
            ISender sender,
            IWebHostEnvironment env,
            CancellationToken ct) =>
        {
            // Body wins; fall back to the cookie (only reachable if a future cookie scope
            // includes /logout). pos-web / mobile send the token in the body; admin-web
            // sends its in-memory token when it still has one (fresh login), and nothing
            // after a hard reload — in which case we just clear the cookie below.
            var rawRefresh = !string.IsNullOrWhiteSpace(req.RefreshToken)
                ? req.RefreshToken!
                : ctx.Request.Cookies[RefreshCookieName];

            // Revoke the token family only when we actually have a token. The validator
            // requires a non-empty token, so skip the command entirely when there is none.
            if (!string.IsNullOrWhiteSpace(rawRefresh))
                await sender.Send(new LogoutCommand(rawRefresh), ct);

            // Always clear the HttpOnly cookie on logout. Append with Max-Age=0 deletes it
            // by name+path even though the cookie is scoped to the refresh path (not /logout).
            ClearRefreshCookie(ctx, env.IsDevelopment());
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Logged out successfully." } });
        })
        .WithName("Logout")
        .RequireAuthorization();

        // POST /api/v1/auth/password/forgot
        auth.MapPost("/password/forgot", async (
            ForgotPasswordRequest req,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(new ForgotPasswordCommand(req.Identifier, req.IdentifierType), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "If an account exists, a reset link has been sent." } });
        })
        .WithName("ForgotPassword")
        .AllowAnonymous();

        // POST /api/v1/auth/password/reset
        auth.MapPost("/password/reset", async (
            ResetPasswordRequest req,
            ISender sender,
            CancellationToken ct) =>
        {
            await sender.Send(new ResetPasswordCommand(req.Token, req.NewPassword), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Password reset successfully." } });
        })
        .WithName("ResetPassword")
        .AllowAnonymous();

        // GET /api/v1/auth/invite/{token} — validate an invitation, return who it's for
        auth.MapGet("/invite/{token}", async (string token, ISender sender, CancellationToken ct) =>
        {
            var preview = await sender.Send(new GetInvitePreviewQuery(token), ct);
            return Results.Ok(new SingleResponse<InvitePreviewDto> { Status = true, Data = preview });
        })
        .WithName("GetInvitePreview")
        .AllowAnonymous();

        // POST /api/v1/auth/accept-invite — set password + activate via invitation token
        auth.MapPost("/accept-invite", async (AcceptInviteRequest req, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new AcceptInviteCommand(req), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Your account is now active. You can sign in." } });
        })
        .WithName("AcceptInvite")
        .AllowAnonymous();

        return group;
    }
}
