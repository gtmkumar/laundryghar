using core.Application.Identity.Auth.Commands.OtpSend;
using core.Application.Identity.Signup.Commands;
using core.Application.Identity.Signup.Dtos;
using core.Application.Identity.Signup.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Provider self-signup — PLATFORM_STRATEGY.md §9's funnel entry: "business details + phone OTP
/// (GSTIN optional) → pick vertical template + plan (trial N days)".
///
/// <para>Entirely anonymous, which is the point: until now a brand could only be created by a
/// platform admin in the back office, so §7's "live on a sub-domain in minutes" had no front door.</para>
///
/// <para>Rate-limited via the <c>auth</c> policy — this endpoint creates tenants, so it is the most
/// attractive thing on the platform to abuse.</para>
/// </summary>
public class Signup : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/signup";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Signup");

        group.MapGet(Templates, "templates").AllowAnonymous();
        group.MapPost(Start, "start").AllowAnonymous().RequireRateLimiting("auth");
        group.MapPost(Complete, "complete").AllowAnonymous().RequireRateLimiting("auth");
    }

    /// <summary>The shop window: which businesses can be launched.</summary>
    public static async Task<IResult> Templates(IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.QueryAsync(new GetSignupTemplatesQuery(), ct);
        return Results.Ok(new SingleResponse<IReadOnlyList<SignupTemplateDto>> { Status = true, Data = data });
    }

    /// <summary>
    /// Step 1 — send the OTP to the phone that will own the account.
    ///
    /// <para>Answers identically whether or not the number is already registered. An endpoint that
    /// said "this number already has an account" would be a free membership oracle for anyone with a
    /// list of phone numbers; the duplicate is caught at <see cref="Complete"/>, after the caller has
    /// proved they hold the number.</para>
    /// </summary>
    public static async Task<IResult> Start(
        SignupStartRequest req, HttpContext ctx, IDispatcher dispatcher, CancellationToken ct)
    {
        var result = await dispatcher.SendAsync(new OtpSendCommand(
            req.PhoneE164, "phone", OtpPurpose.Signup,
            ctx.Connection.RemoteIpAddress?.ToString(),
            ctx.Request.Headers.UserAgent.ToString()), ct);

        return Results.Ok(new SingleResponse<SignupStartResponse>
        {
            Status = true,
            Data = new SignupStartResponse("Verification code sent.", result.ExpiresAt),
        });
    }

    /// <summary>Step 2 — verify the code and provision the whole business in one transaction.</summary>
    public static async Task<IResult> Complete(
        SignupCompleteRequest req, HttpContext ctx, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = await dispatcher.SendAsync(
            new CompleteSignupCommand(req, ctx.Connection.RemoteIpAddress?.ToString()), ct);

        return Results.Ok(new SingleResponse<SignupCompleteResponse> { Status = true, Data = data });
    }
}
