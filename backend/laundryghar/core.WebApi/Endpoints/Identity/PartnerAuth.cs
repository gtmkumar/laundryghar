using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Commands.OtpSend;
using core.Application.Identity.Auth.Commands.PartnerOtpVerify;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// RaaS partner mobile auth endpoints under /api/v1/partner/auth (issue #14, MVP-6).
/// A partner is a separate actor (docs/rbac.md §9): OTP login mints a <c>token_use=partner</c>
/// access token carrying partner_id + partner_role — no brand context, no refresh token (MVP).
/// Both endpoints are anonymous + carry the "auth" rate-limiting policy. The host allow-lists
/// /api/v1/partner/auth/otp for a pre-auth RLS bypass so these tenant-context-less lookups resolve
/// the partner user before any partner context exists (each query is keyed to the caller's phone).
///
/// POST /api/v1/partner/auth/otp/send    (anon)
/// POST /api/v1/partner/auth/otp/verify  (anon)
/// </summary>
public class PartnerAuth : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/partner/auth";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Partner Auth").RequireRateLimiting("auth");

        // POST /api/v1/partner/auth/otp/send
        group.MapPost("/otp/send", async (
            PartnerOtpSendRequest req,
            HttpContext ctx,
            ICoreDbContext db,
            IDispatcher dispatcher,
            IOptions<OtpSettings> otpOptions,
            CancellationToken ct) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var ua = ctx.Request.Headers.UserAgent.ToString();

            // Only send to a registered partner user. When the phone is unknown we still return an
            // identical 200 (no send) so the endpoint is not a partner-enumeration oracle.
            var isPartner = await db.PartnerUsers
                .AnyAsync(pu => pu.PhoneE164 == req.Phone, ct);

            if (!isPartner)
            {
                var decoy = new OtpSentResponse(
                    "OTP sent successfully.",
                    DateTimeOffset.UtcNow.AddMinutes(otpOptions.Value.TtlMinutes));
                return Results.Ok(new SingleResponse<OtpSentResponse> { Status = true, Data = decoy });
            }

            var result = await dispatcher.SendAsync(
                new OtpSendCommand(req.Phone, "phone", OtpPurpose.PartnerLogin, ip, ua), ct);
            return Results.Ok(new SingleResponse<OtpSentResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<PartnerOtpSendRequest>>()
        .WithName("PartnerOtpSend")
        .Produces<SingleResponse<OtpSentResponse>>()
        .AllowAnonymous();

        // POST /api/v1/partner/auth/otp/verify
        group.MapPost("/otp/verify", async (
            PartnerOtpVerifyRequest req,
            IDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var result = await dispatcher.SendAsync(
                new PartnerOtpVerifyCommand(req.Phone, req.Code), ct);
            return Results.Ok(new SingleResponse<PartnerTokenResponse> { Status = true, Data = result });
        })
        .AddEndpointFilter<ValidationFilter<PartnerOtpVerifyRequest>>()
        .WithName("PartnerOtpVerify")
        .Produces<SingleResponse<PartnerTokenResponse>>()
        .AllowAnonymous();
    }
}
