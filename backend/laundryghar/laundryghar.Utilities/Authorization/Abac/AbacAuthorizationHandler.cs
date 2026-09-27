using System.Security.Claims;
using laundryghar.Utilities.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// Marker requirement added alongside <c>PermissionRequirement</c> on every permission policy.
/// Carrying no data: what to evaluate comes from the endpoint's own metadata (A4.1), so one
/// requirement instance serves every endpoint.
/// </summary>
public sealed class AbacRequirement : IAuthorizationRequirement;

/// <summary>Carries the PDP's explanation out to <c>ApiAuthorizationResultHandler</c> (A3.4).</summary>
public sealed class AbacDeniedFailureReason : AuthorizationFailureReason
{
    public AbacDeniedFailureReason(
        AuthorizationHandler<AbacRequirement> handler, AbacTarget target, AbacDecision decision)
        : base(handler, decision.Reason)
    {
        Target = target;
        Decision = decision;
    }

    public AbacTarget Target { get; }
    public AbacDecision Decision { get; }
}

/// <summary>
/// The PEP (A4.2). Composes with the existing RBAC gate rather than replacing it: ASP.NET Core
/// requires EVERY requirement in a policy to be satisfied, so a permission policy now means
/// "hold the code AND survive the policy rows". The coarse code check stays first and cheapest, and
/// remains the thing that keeps "what can this person do?" enumerable for the admin console.
///
/// <para><b>Three ways this handler succeeds without deciding anything</b>, each deliberate:</para>
/// <list type="bullet">
/// <item>the engine is disabled — nothing changes for anyone;</item>
/// <item>the endpoint declares no <see cref="AbacResourceAttribute"/> — endpoints are cut over one
/// at a time, and an un-tagged endpoint must behave exactly as it did before;</item>
/// <item>shadow mode — the decision is computed and logged, and then ignored. This is what makes
/// A6.2's parity diff possible on real traffic without any risk of denying it.</item>
/// </list>
///
/// <para><b>A failure to evaluate is a success here, not a denial.</b> If the PDP itself throws, the
/// request falls back to the RBAC gate that already guarded it. That is the one place this engine
/// deliberately does not fail closed, because failing closed on an unexpected exception would let a
/// bug in the new layer take down endpoints the old layer was protecting perfectly well. The
/// exception is logged at Error, and once a module is cut over its parity window is what proves the
/// path is exercised rather than silently throwing.</para>
/// </summary>
public sealed class AbacAuthorizationHandler : AuthorizationHandler<AbacRequirement>
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IAbacAuthorizationService _abac;
    private readonly ILogger<AbacAuthorizationHandler> _log;

    public AbacAuthorizationHandler(
        IHttpContextAccessor accessor,
        IAbacAuthorizationService abac,
        ILogger<AbacAuthorizationHandler> log)
    {
        _accessor = accessor;
        _abac = abac;
        _log = log;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AbacRequirement requirement)
    {
        var http = _accessor.HttpContext;
        var target = http?.GetAbacTarget();

        if (target is null)
        {
            context.Succeed(requirement);
            return;
        }

        try
        {
            var result = await _abac.EvaluateAsync(
                target.ResourceType, target.Action,
                new AbacResourceRef(target.ResourceType, target.ResourceId),
                http!.RequestAborted,
                rbacAllowed: RbacVerdict(context));

            if (result.IsBlocked)
            {
                context.Fail(new AbacDeniedFailureReason(this, target, result.Decision));
                return;
            }

            context.Succeed(requirement);
        }
        catch (Exception e)
        {
            _log.LogError(e,
                "ABAC evaluation threw for {Resource}/{Action}; falling back to the RBAC gate.",
                target.ResourceType, target.Action);
            context.Succeed(requirement);
        }
    }

    /// <summary>
    /// What the EXISTING permission gate decides for this same request (A6.2).
    ///
    /// <para>The handler can answer this because <see cref="AuthorizationHandlerContext"/> exposes
    /// every requirement in the policy, not just its own — so the permission requirement sitting
    /// beside <see cref="AbacRequirement"/> is right there. That is what makes the parity harness
    /// free: no traffic replay, no sampling, no second environment. One real request, both verdicts,
    /// one row.</para>
    ///
    /// <para>The logic below is a deliberate duplicate of <c>PermissionHandler</c>'s gates 1 and 2.
    /// It is NOT a call into that handler, because a handler records its answer by mutating the
    /// context — invoking it here would make the request satisfy the permission requirement as a
    /// side effect of measuring it. Step-up (gate 3) is excluded on purpose: a step-up denial means
    /// "re-verify and retry", not "you may not", and counting it as an RBAC denial would fill the
    /// parity report with rows that resolve themselves a second later.</para>
    ///
    /// <para>Returns null when the policy carries no permission requirement — there is nothing to
    /// compare against, and a fabricated comparison would be worse than an absent one.</para>
    /// </summary>
    private static bool? RbacVerdict(AuthorizationHandlerContext context)
    {
        var codes = context.Requirements
            .OfType<PermissionRequirement>()
            .Select(r => r.PermissionCode)
            .Concat(context.Requirements
                .OfType<AnyPermissionRequirement>()
                .SelectMany(r => r.PermissionCodes))
            .ToList();

        if (codes.Count == 0) return null;

        var tokenUse = context.User.FindFirstValue("token_use");
        if (!string.Equals(tokenUse, TokenClaims.TokenUseValue, StringComparison.Ordinal))
            return false;

        if (context.User.FindFirstValue("user_type") == SharedDataModel.Enums.UserType.PlatformAdmin)
            return true;

        var held = (context.User.FindFirstValue("permissions") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(PermissionAlias.Canonical)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Any-of semantics: one PermissionRequirement is a single code, an AnyPermissionRequirement
        // is satisfied by any of its codes, and a policy carrying both needs one from each. Flattening
        // to "holds at least one" matches the single-code case exactly and is the OR-set case by
        // construction; a policy with two independent single-code requirements does not exist today.
        return codes.Any(c => held.Contains(PermissionAlias.Canonical(c)));
    }
}
