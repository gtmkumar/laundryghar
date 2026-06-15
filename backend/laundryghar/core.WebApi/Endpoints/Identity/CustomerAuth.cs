using System.Security.Claims;
using core.Application.Identity.Auth.Commands.CustomerLogout;
using core.Application.Identity.Auth.Commands.CustomerOtpSend;
using core.Application.Identity.Auth.Commands.CustomerOtpVerify;
using core.Application.Identity.Auth.Commands.CustomerRefresh;
using core.Application.Identity.Auth.Dtos;
using core.Application.Identity.Auth.Queries.GetCustomerMe;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Validation;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Customer mobile auth endpoints under /api/v1/customer/auth.
/// Brand resolution order (in-handler): X-Brand-Id header → brandCode body field → DefaultBrandCode config → "LG-MAIN".
/// All endpoints carry the "auth" rate-limiting policy.
/// /logout and /me are CustomerOnly — require token_use=customer (system tokens rejected).
///
/// POST /api/v1/customer/auth/otp/send    (anon)
/// POST /api/v1/customer/auth/otp/verify  (anon)
/// POST /api/v1/customer/auth/refresh     (anon)
/// POST /api/v1/customer/auth/logout      (CustomerOnly)
/// GET  /api/v1/customer/auth/me          (CustomerOnly)
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
            // JwtBearer maps "sub" → ClaimTypes.NameIdentifier; fall back to the raw "sub" claim.
            var subClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? ctx.User.FindFirstValue("sub");
            if (!Guid.TryParse(subClaim, out var customerId))
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

    /// <summary>Reads the X-Brand-Id header as a Guid, returning null when absent or unparseable.</summary>
    private static Guid? ReadBrandIdHeader(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue("X-Brand-Id", out var headerVal)
        && Guid.TryParse(headerVal, out var headerId)
            ? headerId
            : null;
}
