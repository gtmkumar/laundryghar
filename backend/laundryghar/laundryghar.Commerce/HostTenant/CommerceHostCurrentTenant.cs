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
///   • Worker hosted-service scope → no HttpContext (scopes are created via
///     IServiceScopeFactory.CreateAsyncScope() off the request pipeline). We return the
///     worker semantics: no brand/franchise/store context and BypassRls = true, so the
///     interceptor emits SET app.bypass_rls = 'true'. Identical to the standalone
///     WorkerCurrentTenant behaviour.
///
/// This preserves RLS for the HTTP lanes (we never blanket-bypass) while giving the worker
/// the cross-tenant visibility it needs to drain the outboxes — without a second DbContext
/// or a second connection string. Both lanes use ConnectionStrings:Default (app_user); the
/// worker's cross-tenant access is granted by the bypass_rls session flag, exactly as in the
/// standalone Worker today (whose Default connection was also app_user).
/// </summary>
public sealed class CommerceHostCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _accessor;

    public CommerceHostCurrentTenant(IHttpContextAccessor accessor) => _accessor = accessor;

    private bool IsHttpLane => _accessor.HttpContext is not null;

    public Guid? BrandId
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx is null) return null; // worker lane — no brand context

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
    /// HTTP lane: true only when TenantResolutionMiddleware flagged a platform admin
    /// (or the anonymous Razorpay webhook set Items["bypass_rls"]).
    /// Worker lane: always true (cross-tenant outbox drain).
    /// </summary>
    public bool BypassRls =>
        _accessor.HttpContext is { } ctx
            ? ctx.Items.ContainsKey("bypass_rls") && ctx.Items["bypass_rls"] is true
            : true;

    private Guid? ParseGuid(string claimType) =>
        Guid.TryParse(_accessor.HttpContext?.User?.FindFirstValue(claimType), out var g) ? g : null;
}
