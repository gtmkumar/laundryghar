using laundryghar.Commerce.HostTenant;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace laundryghar.Commerce.Tests.HostTenant;

/// <summary>
/// SEC-1 regression tests: <see cref="CommerceHostCurrentTenant"/> must fail CLOSED.
///
/// The old implementation inferred "this is the worker" from the ABSENCE of an HttpContext
/// and granted BypassRls = true — fail-open. These tests pin the corrected behavior:
///   • No HttpContext AND no worker marker        → BypassRls == false, BrandId == null.
///   • No HttpContext BUT positive worker marker  → BypassRls == true  (cross-tenant drain).
///   • HttpContext present, platform-admin override (Items["bypass_rls"]=true) → true.
///   • HttpContext present, normal user (no override) → false (RLS stays enforced).
///
/// The worker marker is an AsyncLocal flag. Each marker-setting case runs inside its own
/// Task.Run so the AsyncLocal copy-on-write boundary keeps the flag from leaking into other
/// tests / the test runner's flow.
/// </summary>
public sealed class CommerceHostCurrentTenantTests
{
    private static CommerceHostCurrentTenant ForHttp(HttpContext? ctx)
    {
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new CommerceHostCurrentTenant(accessor);
    }

    // ── No HttpContext, no worker marker → fail closed ────────────────────────

    [Fact]
    public async Task NoHttpContext_NoWorkerMarker_BypassRls_IsFalse()
    {
        // Run in an isolated flow that does NOT set the worker marker.
        var bypass = await Task.Run(() =>
        {
            var tenant = ForHttp(null);
            return tenant.BypassRls;
        });

        Assert.False(bypass);
    }

    [Fact]
    public async Task NoHttpContext_NoWorkerMarker_BrandId_IsNull()
    {
        var brand = await Task.Run(() =>
        {
            var tenant = ForHttp(null);
            return tenant.BrandId;
        });

        Assert.Null(brand);
    }

    // ── No HttpContext, positive worker marker → cross-tenant bypass ──────────

    [Fact]
    public async Task NoHttpContext_WithWorkerMarker_BypassRls_IsTrue()
    {
        var bypass = await Task.Run(() =>
        {
            WorkerScope.MarkWorkerScope(); // positive assertion: trusted worker scope
            var tenant = ForHttp(null);
            return tenant.BypassRls;
        });

        Assert.True(bypass);
    }

    [Fact]
    public async Task CreateWorkerAsyncScope_SetsMarker_SoBypassRls_IsTrue()
    {
        // Exercise the actual extension worker services call, end to end.
        var provider = new ServiceCollection().BuildServiceProvider();
        var factory = provider.GetRequiredService<IServiceScopeFactory>();

        var bypass = await Task.Run(async () =>
        {
            await using var scope = factory.CreateWorkerAsyncScope();
            var tenant = ForHttp(null);
            return tenant.BypassRls;
        });

        Assert.True(bypass);
    }

    // ── HttpContext present, platform-admin override → bypass ────────────────

    [Fact]
    public void HttpContext_PlatformAdminOverride_BypassRls_IsTrue()
    {
        var ctx = new DefaultHttpContext();
        ctx.Items["bypass_rls"] = true; // set by TenantResolutionMiddleware for platform admins
        var tenant = ForHttp(ctx);

        Assert.True(tenant.BypassRls);
    }

    // ── HttpContext present, normal user → RLS enforced ──────────────────────

    [Fact]
    public void HttpContext_NormalUser_BypassRls_IsFalse()
    {
        var ctx = new DefaultHttpContext(); // no bypass_rls item
        var tenant = ForHttp(ctx);

        Assert.False(tenant.BypassRls);
    }

    [Fact]
    public void HttpContext_BypassItemNotBoolTrue_BypassRls_IsFalse()
    {
        // Defensive: a non-true value in the item must NOT bypass.
        var ctx = new DefaultHttpContext();
        ctx.Items["bypass_rls"] = "true"; // string, not bool true
        var tenant = ForHttp(ctx);

        Assert.False(tenant.BypassRls);
    }

    // ── HttpContext takes precedence over an ambient worker marker ───────────

    [Fact]
    public async Task HttpContextPresent_IgnoresWorkerMarker_NormalUserStaysEnforced()
    {
        var enforced = await Task.Run(() =>
        {
            WorkerScope.MarkWorkerScope();      // even if a marker leaked in,
            var ctx = new DefaultHttpContext(); // an HTTP request must follow HTTP rules
            var tenant = ForHttp(ctx);
            return tenant.BypassRls;            // → false (no bypass_rls item)
        });

        Assert.False(enforced);
    }
}
