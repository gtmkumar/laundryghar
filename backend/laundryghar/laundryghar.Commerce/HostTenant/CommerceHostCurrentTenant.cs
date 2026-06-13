using System.Security.Claims;
using laundryghar.SharedDataModel.Contracts;

namespace laundryghar.Commerce.HostTenant;

/// <summary>
/// Single <see cref="ICurrentTenant"/> implementation for the consolidated Commerce host,
/// which runs BOTH HTTP request lanes (Commerce / Finance / Analytics) and the in-process
/// Worker hosted services in the same DI container.
///
/// Why one dispatching implementation instead of two registrations:
///   The <c>RlsConnectionInterceptor</c> (registered Scoped by AddSharedDataModel) depends on
///   exactly one Scoped <see cref="ICurrentTenant"/>. There is only ONE such registration per
///   container. We therefore dispatch at resolution time on the presence of an ambient
///   <see cref="HttpContext"/>:
///
///   • HTTP request scope  → HttpContext is present (set by the framework). We read the RLS
///     tenant from the validated JWT claims and honour the platform-admin overrides set by
///     TenantResolutionMiddleware (X-Brand-Id → brand_id_override, bypass_rls). RLS stays
///     fully enforced for normal users; only platform admins bypass — exactly as today.
///
///   • Trusted worker scope → flagged POSITIVELY via <see cref="WorkerScope"/> (an
///     AsyncLocal marker the BackgroundServices set when they create their DI scope with
///     <c>CreateWorkerAsyncScope()</c>). Only then do we return worker semantics: no
///     brand/franchise/store context and BypassRls = true, so the interceptor emits
///     SET app.bypass_rls = 'true'. Identical cross-tenant visibility to the standalone
///     WorkerCurrentTenant — but granted by an explicit assertion, not by inference.
///
/// FAIL-CLOSED (SEC-1): the previous implementation inferred "this is the worker" from the
/// ABSENCE of an HttpContext, which is fail-open — a fire-and-forget Task.Run off an HTTP
/// request loses HttpContext and would have silently bypassed RLS across all tenants. We now
/// require the positive worker marker. If there is no HttpContext AND no worker marker, we
/// grant NOTHING: no brand context and BypassRls = false (RLS stays enforced; queries see no
/// rows rather than all rows).
///
/// This preserves RLS for the HTTP lanes (we never blanket-bypass) while giving genuine
/// worker scopes the cross-tenant visibility they need to drain the outboxes — without a
/// second DbContext or connection string. Both lanes use ConnectionStrings:Default (app_user);
/// the worker's cross-tenant access is granted by the bypass_rls session flag.
/// </summary>
public sealed class CommerceHostCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _accessor;

    public CommerceHostCurrentTenant(IHttpContextAccessor accessor) => _accessor = accessor;

    /// <summary>HTTP lane = an ambient HttpContext is present (set by the framework pipeline).</summary>
    private bool IsHttpLane => _accessor.HttpContext is not null;

    public Guid? BrandId
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx is null)
                return null; // worker lane → no brand; non-worker context-less flow → no brand (fail closed)

            // Platform-admin brand override (set by TenantResolutionMiddleware from X-Brand-Id).
            if (ctx.Items.TryGetValue("brand_id_override", out var v) && v is Guid g && g != Guid.Empty)
                return g;

            return ParseGuid("brand_id");
        }
    }

    public Guid? FranchiseId => IsHttpLane ? ParseGuid("franchise_id") : null;
    public Guid? StoreId     => IsHttpLane ? ParseGuid("store_id")     : null;
    public Guid? UserId      => IsHttpLane ? ParseGuid(ClaimTypes.NameIdentifier) : null;

    /// <summary>
    /// True ONLY when:
    ///   (a) this is a trusted worker scope (positive <see cref="WorkerScope"/> marker), OR
    ///   (b) an authenticated platform admin / the anonymous Razorpay webhook set
    ///       HttpContext.Items["bypass_rls"] = true via TenantResolutionMiddleware / the endpoint.
    /// In ALL other cases — including a context-less flow with NO worker marker — returns false,
    /// so RLS stays enforced (fail closed).
    /// </summary>
    public bool BypassRls
    {
        get
        {
            if (_accessor.HttpContext is { } ctx)
                return ctx.Items.TryGetValue("bypass_rls", out var v) && v is true;

            // No HttpContext: only a positively-marked worker scope may bypass.
            return WorkerScope.IsWorkerScope;
        }
    }

    private Guid? ParseGuid(string claimType) =>
        Guid.TryParse(_accessor.HttpContext?.User?.FindFirstValue(claimType), out var g) ? g : null;
}
