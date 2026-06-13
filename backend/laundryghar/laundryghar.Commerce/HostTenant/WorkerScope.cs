namespace laundryghar.Commerce.HostTenant;

/// <summary>
/// Positive, fail-closed marker that a DI scope was created by a trusted in-process
/// Worker hosted service (NOT by an HTTP request).
///
/// Why this exists (SEC-1):
///   <see cref="CommerceHostCurrentTenant"/> previously inferred "this is the worker"
///   from the ABSENCE of an <see cref="Microsoft.AspNetCore.Http.HttpContext"/>. That is
///   fail-OPEN: any fire-and-forget <c>Task.Run</c> spawned off an HTTP request loses the
///   ambient HttpContext and would then have silently run with <c>BypassRls = true</c> —
///   reading and writing across ALL tenants.
///
///   We replace the negative inference with a POSITIVE assertion: a worker scope must
///   explicitly flag itself as trusted via <see cref="MarkWorkerScope"/>. The flag is held
///   in an <see cref="AsyncLocal{T}"/> so it flows to the scoped services resolved within
///   that scope (and to the async work done inside it) but does NOT leak to sibling async
///   flows. <see cref="CommerceHostCurrentTenant.BypassRls"/> grants cross-tenant access
///   ONLY when this flag is set (worker) or an authenticated platform-admin set the
///   per-request override. Everything else fails closed (RLS enforced / no brand).
/// </summary>
public static class WorkerScope
{
    private static readonly AsyncLocal<bool> _isWorkerScope = new();

    /// <summary>True when the current async flow is inside a trusted worker scope.</summary>
    public static bool IsWorkerScope => _isWorkerScope.Value;

    /// <summary>
    /// Flags the current async flow as a trusted worker scope. Call this immediately after
    /// creating the worker's DI scope, BEFORE resolving any tenant-aware service
    /// (<c>LaundryGharDbContext</c>, <c>ICurrentTenant</c>, …) from it.
    /// </summary>
    public static void MarkWorkerScope() => _isWorkerScope.Value = true;

    /// <summary>
    /// Creates an async DI scope and flags it as a trusted worker scope in one step.
    /// Use this from every Worker / background <c>BackgroundService</c> in place of
    /// <c>IServiceScopeFactory.CreateAsyncScope()</c> so cross-tenant access is granted
    /// by a positive marker, never by the mere absence of an HttpContext.
    /// </summary>
    public static AsyncServiceScope CreateWorkerAsyncScope(this IServiceScopeFactory factory)
    {
        var scope = factory.CreateAsyncScope();
        MarkWorkerScope();
        return scope;
    }

    /// <summary>
    /// Synchronous variant of <see cref="CreateWorkerAsyncScope"/> for the few worker
    /// services that use the non-async scope API.
    /// </summary>
    public static IServiceScope CreateWorkerScope(this IServiceScopeFactory factory)
    {
        var scope = factory.CreateScope();
        MarkWorkerScope();
        return scope;
    }
}
