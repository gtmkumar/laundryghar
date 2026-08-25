using System.Security.Claims;
using System.Text.Json;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// §9: "Suspension = <b>login-only mode</b> (owner can see invoices and pay; operations frozen) —
/// never silent data loss."
///
/// <para>The shape of this feature is an allow-list, and the tests are written around why. A blanket
/// block would strand the owner outside the only door that reopens their account: they could not sign
/// in, could not see the invoice, could not pay it. So the interesting assertions are not "operations
/// are blocked" — they are "the way back in is still open".</para>
/// </summary>
public class BrandSuspensionMiddlewareTests
{
    private static readonly Guid Brand = Guid.NewGuid();

    // 1 ── operations are frozen for a suspended brand.
    [Fact]
    public async Task An_operational_call_is_blocked_for_a_suspended_brand()
    {
        var context = Ctx("/api/v1/admin/orders", status: "suspended");

        await Run(context);

        Assert.Equal(StatusCodes.Status402PaymentRequired, context.Response.StatusCode);
        Assert.Equal(BrandSuspensionMiddleware.ResponseMessage,
                     Message(context).GetProperty("responseMessage").GetString());
        // Body must agree with the wire — a 402 carrying errorTypeCode 403 tells a client the
        // owner lacks a ROLE when in fact the brand owes us money, and those have opposite fixes.
        Assert.Equal(context.Response.StatusCode,
                     Message(context).GetProperty("errorTypeCode").GetInt32());
        Assert.False(NextWasCalled(context));
    }

    // 2 ── THE POINT: every step of the way back in stays open. Sign in, read the invoice, pay it,
    //      and let the gateway tell us it landed. Block any one of these and suspension becomes a
    //      trap rather than a pause.
    [Theory]
    [InlineData("/api/v1/auth/password/login")]      // sign in
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/step-up/verify")]
    [InlineData("/api/v1/admin/entitlements/bundles")]                       // see the tiers
    [InlineData("/api/v1/admin/entitlements/brands/x/platform-subscription")]// see the invoice
    [InlineData("/api/v1/webhooks/razorpay-paylink")]                        // the payment landing
    [InlineData("/api/v1/admin/navigator")]                                  // render a shell, not a blank page
    [InlineData("/health")]
    public async Task The_route_back_in_stays_open(string path)
    {
        var context = Ctx(path, status: "suspended");

        await Run(context);

        Assert.True(NextWasCalled(context), $"{path} must remain reachable while suspended");
        Assert.NotEqual(StatusCodes.Status402PaymentRequired, context.Response.StatusCode);
    }

    // 2b ── §9's OTHER login-only state: a wind-down in progress. Operations frozen, and — the part
    //       that matters — export and withdraw still reachable. A retention window the customer
    //       cannot export from is deletion with a delay, which is the opposite of what §8.2 promises.
    [Fact]
    public async Task A_cancelled_brand_is_frozen_but_can_still_export_and_change_its_mind()
    {
        var blocked = Ctx("/api/v1/admin/orders", status: "cancelled");
        await Run(blocked);

        Assert.Equal(StatusCodes.Status402PaymentRequired, blocked.Response.StatusCode);
        // A DISTINCT code from brand_suspended: both are 402, but one is fixed by paying an invoice
        // and the other by withdrawing a cancellation. Telling a leaving customer to settle a bill
        // would be worse than saying nothing.
        Assert.Equal(BrandSuspensionMiddleware.CancelledResponseMessage,
                     Message(blocked).GetProperty("responseMessage").GetString());
        Assert.Equal(blocked.Response.StatusCode,
                     Message(blocked).GetProperty("errorTypeCode").GetInt32());

        foreach (var path in new[]
                 {
                     "/api/v1/admin/cancellation",
                     "/api/v1/admin/cancellation/export",
                     "/api/v1/admin/cancellation/withdraw",
                     "/api/v1/auth/password/login",
                 })
        {
            var open = Ctx(path, status: "cancelled");
            await Run(open);
            Assert.True(NextWasCalled(open), $"{path} must stay reachable during a wind-down");
        }
    }

    // 2c ── an ARCHIVED brand is NOT gated here. Its data is already gone; there is nothing left to
    //       freeze, and gating it would only produce a confusing 402 on an empty tenant.
    [Fact]
    public async Task An_archived_brand_is_not_gated_by_this_middleware()
    {
        var context = Ctx("/api/v1/admin/orders", status: "archived");

        await Run(context);

        Assert.True(NextWasCalled(context));
    }

    // 3 ── an ACTIVE brand is untouched — the middleware must be invisible in normal operation.
    [Fact]
    public async Task An_active_brand_is_never_gated()
    {
        var context = Ctx("/api/v1/admin/orders", status: "active");

        await Run(context);

        Assert.True(NextWasCalled(context));
    }

    // 4 ── platform admins keep working on a suspended tenant. They are the ones who reinstate it;
    //      locking them out would make suspension unrecoverable from our side.
    [Fact]
    public async Task A_platform_admin_still_operates_on_a_suspended_brand()
    {
        var context = Ctx("/api/v1/admin/orders", status: "suspended", userType: UserType.PlatformAdmin);

        await Run(context);

        Assert.True(NextWasCalled(context));
    }

    // 5 ── anonymous traffic has no brand to suspend.
    [Fact]
    public async Task Anonymous_traffic_is_not_gated()
    {
        var context = Ctx("/api/v1/public/banners", status: "suspended", authenticated: false);

        await Run(context);

        Assert.True(NextWasCalled(context));
    }

    // 6 ── FAIL OPEN. An unknown status must never take a working tenant offline; the worst case of
    //      guessing wrong here is a suspended brand trading briefly, which beats an outage for every
    //      healthy one.
    [Theory]
    [InlineData(null)]        // lookup returned nothing / errored
    [InlineData("archived")]  // a wind-down state the cancellation pipeline owns, not this gate
    public async Task An_unknown_or_non_suspended_status_fails_open(string? status)
    {
        var context = Ctx("/api/v1/admin/orders", status: status);

        await Run(context);

        Assert.True(NextWasCalled(context));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private const string NextFlag = "next-was-called";

    private static async Task Run(HttpContext context) =>
        await new BrandSuspensionMiddleware(ctx =>
        {
            ctx.Items[NextFlag] = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);

    private static bool NextWasCalled(HttpContext context) => context.Items.ContainsKey(NextFlag);

    private static DefaultHttpContext Ctx(
        string path, string? status, string userType = UserType.Staff, bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBrandStatusStore>(new FakeStatusStore(status));

        var claims = new List<Claim>();
        if (authenticated)
        {
            claims.Add(new Claim("user_type", userType));
            claims.Add(new Claim("brand_id", Brand.ToString()));
        }

        // An identity with no authenticationType is treated as unauthenticated by ClaimsIdentity.
        var identity = authenticated ? new ClaimsIdentity(claims, "Bearer") : new ClaimsIdentity();

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        context.Request.Path = path;
        return context;
    }

    private static JsonElement Message(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var doc = JsonDocument.Parse(context.Response.Body);
        return doc.RootElement.GetProperty("message").Clone();
    }

    private sealed class FakeStatusStore(string? status) : IBrandStatusStore
    {
        public Task<string?> GetStatusAsync(Guid brandId, CancellationToken ct = default)
            => Task.FromResult(status);

        public Task<string?> SetCancellationStateAsync(Guid brandId, string newStatus, CancellationToken ct = default)
            => Task.FromResult(status);
    }
}
