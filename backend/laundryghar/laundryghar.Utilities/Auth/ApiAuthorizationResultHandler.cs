using System.Text.Json;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace laundryghar.Utilities.Auth;

/// <summary>
/// Turns an authorization denial into a structured, machine-readable response instead of the bare
/// empty 403 the framework default emits. Two cases are recognised; everything else delegates to the
/// default. Register once per host.
///
/// <list type="number">
/// <item><b>403 <c>step_up_required</c></b> (§8) — the caller HOLDS the permission but has no fresh
/// OTP proof for a high/critical action. Carries the permission code so the client can prompt, call
/// /auth/step-up/verify, swap its token and retry.</item>
///
/// <item><b>402 <c>feature_not_in_plan</c></b> (PLATFORM_STRATEGY.md §5) — the denial was caused by
/// the brand's PLAN, not its permissions. Carries the feature key and an upgrade link.</item>
/// </list>
///
/// <para><b>Why 402 needs a lookup at all.</b> The entitlement filter strips un-entitled permissions
/// from the token when it is minted, so a plan denial and a permission denial arrive here looking
/// exactly alike — the permission is simply absent. The token carries the un-licensed FEATURE keys
/// (<c>ent_off</c>); this handler maps the required permission to its feature via
/// <see cref="IFeatureCatalog"/> and, if that feature is one the brand does not own, answers 402.
/// Otherwise the denial really is about permissions and stays 403.</para>
///
/// <para><b>Order matters:</b> step-up is checked FIRST. A step-up denial means the caller has both
/// the permission and the plan and merely needs to re-verify — reporting that as "upgrade your plan"
/// would send them to buy something they already own.</para>
/// </summary>
public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <summary>Where a client sends an owner to buy the missing feature.</summary>
    public const string UpgradePath = "/settings?tab=plan";

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden || context.Response.HasStarted)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        // ── 1. Step-up first: they own the permission AND the plan, they just need to re-verify.
        var stepUp = authorizeResult.AuthorizationFailure?.FailureReasons
            .OfType<StepUpRequiredFailureReason>()
            .FirstOrDefault();

        if (stepUp is not null)
        {
            await WriteAsync(context, StatusCodes.Status403Forbidden, ErrorMessageEnum.Forbidden,
                "step_up_required", "step_up_required", [stepUp.PermissionCode]);
            return;
        }

        // ── 2. Otherwise: was this denial caused by the PLAN rather than by permissions?
        //      Only worth asking when the token actually reports missing features.
        var unlicensed = FeatureNotInPlan.UnlicensedFeatures(context.User);
        if (unlicensed.Count > 0)
        {
            var catalog = context.RequestServices.GetService<IFeatureCatalog>();
            if (catalog is not null)
            {
                foreach (var requirement in policy.Requirements.OfType<PermissionRequirement>())
                {
                    var code = PermissionAlias.Canonical(requirement.PermissionCode);
                    var feature = await catalog.FeatureForPermissionAsync(code, context.RequestAborted);

                    if (FeatureNotInPlan.IsUnlicensed(context.User, feature))
                    {
                        // PaymentRequired, not Forbidden: the body's errorTypeCode is what clients
                        // read, and answering 402 on the wire while saying 403 inside erases the
                        // very plan-vs-permission distinction this handler exists to draw.
                        await WriteAsync(context, StatusCodes.Status402PaymentRequired,
                            ErrorMessageEnum.PaymentRequired, FeatureNotInPlan.ResponseMessage,
                            FeatureNotInPlan.ResponseMessage, [feature!, UpgradePath]);
                        return;
                    }
                }
            }
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteAsync(
        HttpContext context, int status, ErrorMessageEnum code,
        string responseMessage, string errorKey, string[] errorValues)
    {
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";

        var payload = new Response
        {
            Status = false,
            Message = new Message
            {
                ErrorTypeCode   = code,
                ResponseMessage = responseMessage,
                ErrorMessage    = new Dictionary<string, string[]> { [errorKey] = errorValues },
            },
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions);
    }
}
