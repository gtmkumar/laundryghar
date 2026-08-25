using System.Security.Claims;
using core.Application.Identity.Auth.Commands.CustomerGoogleSignIn;
using core.Application.Identity.Auth.Commands.CustomerLogout;
using core.Application.Identity.Auth.Commands.CustomerOtpSend;
using core.Application.Identity.Auth.Commands.CustomerOtpVerify;
using core.Application.Identity.Auth.Commands.CustomerPhoneLink;
using core.Application.Identity.Auth.Commands.CustomerPin;
using core.Application.Identity.Auth.Commands.CustomerRefresh;
using core.Application.Identity.Auth.Dtos;
using core.Application.Identity.Auth.Queries.GetCustomerMe;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Validation;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Customer mobile auth endpoints under /api/v1/customer/auth.
/// Brand resolution order (in-handler): X-Brand-Id header → brandCode body field → DefaultBrandCode config → "LG-MAIN".
/// All endpoints carry the "auth" rate-limiting policy.
/// CustomerOnly endpoints require token_use=customer (system tokens rejected).
///
/// POST /api/v1/customer/auth/otp/send          (anon)
/// POST /api/v1/customer/auth/otp/verify        (anon)
/// POST /api/v1/customer/auth/google            (anon)  — Google ID token sign-in / sign-up
/// POST /api/v1/customer/auth/pin/verify        (anon)  — returning-user PIN unlock
/// POST /api/v1/customer/auth/refresh           (anon)
/// POST /api/v1/customer/auth/phone/link/send   (CustomerOnly) — optional phone attach, step 1
/// POST /api/v1/customer/auth/phone/link/verify (CustomerOnly) — optional phone attach, step 2
/// POST /api/v1/customer/auth/pin               (CustomerOnly) — set/replace the unlock PIN
/// POST /api/v1/customer/auth/logout            (CustomerOnly)
/// GET  /api/v1/customer/auth/me                (CustomerOnly)
///
/// Biometric unlock has no endpoint by design: the device authenticates the user locally
/// and then replays its stored refresh token through /refresh.
/// </summary>
public class CustomerAuth : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/customer/auth";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Customer Auth").RequireRateLimiting("auth");

        // POST /api/v1/customer/auth/otp/send
        group.MapPost("/otp/send", async (
            CustomerOtpSendRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var headerBrandId = ReadBrandIdHeader(ctx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(
                new CustomerOtpSendCommand(req.Phone, headerBrandId, req.BrandCode, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpSentResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<CustomerOtpSendRequest>>()
        .WithName("CustomerOtpSend")
        .Produces<SingleResponse<OtpSentResponse>>()
        .AllowAnonymous();

        // POST /api/v1/customer/auth/otp/verify
        group.MapPost("/otp/verify", async (
            CustomerOtpVerifyRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var headerBrandId = ReadBrandIdHeader(ctx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(
                new CustomerOtpVerifyCommand(req.Phone, req.Code, headerBrandId, req.BrandCode, ip, ua), ct);
            return Results.Ok(new SingleResponse<CustomerTokenResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<CustomerOtpVerifyRequest>>()
        .WithName("CustomerOtpVerify")
        .Produces<SingleResponse<CustomerTokenResponse>>()
        .AllowAnonymous();

        // POST /api/v1/customer/auth/google
        // Sign in (or sign up) with a Google ID token obtained by the client. Anonymous:
        // the token IS the credential. Returns the same token envelope as OTP verify, with
        // needsPhone set when the new account has no phone number yet.
        group.MapPost("/google", async (
            CustomerGoogleSignInRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var headerBrandId = ReadBrandIdHeader(ctx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(
                new CustomerGoogleSignInCommand(req.IdToken, headerBrandId, req.BrandCode, ip, ua), ct);
            return Results.Ok(new SingleResponse<CustomerTokenResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<CustomerGoogleSignInRequest>>()
        .WithName("CustomerGoogleSignIn")
        .Produces<SingleResponse<CustomerTokenResponse>>()
        .ProducesProblem(401)
        .AllowAnonymous();

        // POST /api/v1/customer/auth/pin/verify — returning-user unlock without a new OTP.
        group.MapPost("/pin/verify", async (
            CustomerPinVerifyRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var headerBrandId = ReadBrandIdHeader(ctx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(
                new CustomerPinVerifyCommand(req.Identifier, req.Pin, headerBrandId, req.BrandCode, ip, ua), ct);
            return Results.Ok(new SingleResponse<CustomerTokenResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<CustomerPinVerifyRequest>>()
        .WithName("CustomerPinVerify")
        .Produces<SingleResponse<CustomerTokenResponse>>()
        .ProducesProblem(401)
        .AllowAnonymous();

        // POST /api/v1/customer/auth/refresh
        group.MapPost("/refresh", async (
            RefreshTokenRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(
                new CustomerRefreshCommand(req.RefreshToken, ip, ua), ct);
            return Results.Ok(new SingleResponse<CustomerTokenResponse> { Status = true, Data = result });
        })
        .WithName("CustomerRefresh")
        .Produces<SingleResponse<CustomerTokenResponse>>()
        .AllowAnonymous();

        // POST /api/v1/customer/auth/phone/link/send — step 1 of attaching a phone number.
        // CustomerOnly: the account being modified is the caller's own, taken from the token.
        // Uses the verify_phone OTP namespace so the resulting code cannot be replayed as a login.
        group.MapPost("/phone/link/send", async (
            CustomerPhoneLinkSendRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (!TryReadCustomerId(ctx, out var customerId))
                return Results.Unauthorized();

            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            // Brand comes from the caller's own record, not a header or body field.
            var result = await dispatcher.SendAsync(
                new CustomerOtpSendCommand(
                    req.Phone, null, null, ip, ua, OtpPurpose.VerifyPhone,
                    AuthenticatedCustomerId: customerId),
                ct);
            return Results.Ok(new SingleResponse<OtpSentResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<CustomerPhoneLinkSendRequest>>()
        .WithName("CustomerPhoneLinkSend")
        .Produces<SingleResponse<OtpSentResponse>>()
        .RequireAuthorization("CustomerOnly");

        // POST /api/v1/customer/auth/phone/link/verify — step 2, writes the number onto the account.
        group.MapPost("/phone/link/verify", async (
            CustomerPhoneLinkVerifyRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (!TryReadCustomerId(ctx, out var customerId))
                return Results.Unauthorized();

            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var result = await dispatcher.SendAsync(
                new CustomerPhoneLinkVerifyCommand(customerId, req.Phone, req.Code, ip, ua), ct);
            return Results.Ok(new SingleResponse<CustomerPhoneLinkedResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<CustomerPhoneLinkVerifyRequest>>()
        .WithName("CustomerPhoneLinkVerify")
        .Produces<SingleResponse<CustomerPhoneLinkedResponse>>()
        .RequireAuthorization("CustomerOnly");

        // POST /api/v1/customer/auth/pin — set or replace the caller's unlock PIN.
        group.MapPost("/pin", async (
            CustomerPinSetRequest req,
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (!TryReadCustomerId(ctx, out var customerId))
                return Results.Unauthorized();

            await dispatcher.SendAsync(new CustomerPinSetCommand(customerId, req.Pin), ct);
            return Results.Ok(new Response
            {
                Status = true,
                Message = new Message { ResponseMessage = "PIN saved." }
            });
        })
        .AddEndpointFilter<ValidationFilter<CustomerPinSetRequest>>()
        .WithName("CustomerPinSet")
        .RequireAuthorization("CustomerOnly");

        // POST /api/v1/customer/auth/logout
        group.MapPost("/logout", async (
            LogoutRequest req,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            await dispatcher.SendAsync(new CustomerLogoutCommand(req.RefreshToken), ct);
            return Results.Ok(new Response { Status = true, Message = new Message { ResponseMessage = "Logged out." } });
        })
        .WithName("CustomerLogout")
        .RequireAuthorization("CustomerOnly");

        // GET /api/v1/customer/auth/me — CustomerOnly: verifies system tokens are rejected
        group.MapGet("/me", async (
            HttpContext ctx,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (!TryReadCustomerId(ctx, out var customerId))
                return Results.Unauthorized();

            var me = await dispatcher.QueryAsync(new GetCustomerMeQuery(customerId), ct);
            return me is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<CustomerMeResponse> { Status = true, Data = me });
        })
        .WithName("CustomerMe")
        .Produces<SingleResponse<CustomerMeResponse>>()
        .RequireAuthorization("CustomerOnly");
    }

    /// <summary>
    /// Reads the caller's own customer id from the token's subject claim. Endpoints that
    /// mutate an account MUST use this rather than trusting an id in the request body.
    /// JwtBearer maps "sub" → ClaimTypes.NameIdentifier; fall back to the raw "sub" claim.
    /// </summary>
    private static bool TryReadCustomerId(HttpContext ctx, out Guid customerId)
    {
        var subClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? ctx.User.FindFirstValue("sub");
        return Guid.TryParse(subClaim, out customerId);
    }

    /// <summary>Reads the X-Brand-Id header as a Guid, returning null when absent or unparseable.</summary>
    private static Guid? ReadBrandIdHeader(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue("X-Brand-Id", out var headerVal)
        && Guid.TryParse(headerVal, out var headerId)
            ? headerId
            : null;
}
