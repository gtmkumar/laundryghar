using System.Security.Claims;
using System.Text.Json;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// §7: "Support: impersonation with consent + full audit (Platform Support role, read-first)."
///
/// <para>The guard is where three of the four promises are actually kept — read-first, revocable,
/// time-boxed — so these tests are written around the ways each one could quietly fail. The most
/// important is <see cref="A_revoked_grant_stops_working_immediately"/>: a token still valid by
/// signature and expiry must stop being usable the moment the provider says stop. If that check ever
/// starts reading the CLAIM instead of the store, every other test here still passes.</para>
/// </summary>
public class ImpersonationGuardMiddlewareTests
{
    private static readonly Guid Support = Guid.NewGuid();
    private static readonly Guid Brand   = Guid.NewGuid();
    private static readonly Guid Grant   = Guid.NewGuid();

    // 1 ── ordinary traffic never touches the store. If this ever regressed, every request in the
    //      system would pay a database round-trip for a feature almost nobody uses.
    [Fact]
    public async Task An_ordinary_request_passes_through_without_consulting_the_store()
    {
        var store = new FakeStore(null);
        var context = Ctx("/api/v1/admin/orders", "POST", store: store, omitGrantClaim: true);

        await Run(context);

        Assert.True(NextWasCalled(context));
        Assert.Equal(0, store.Calls);
    }

    // 2 ── READ-FIRST. A read-only session may look at anything and change nothing.
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task A_read_only_session_may_read(string method)
    {
        var context = Ctx("/api/v1/admin/orders", method,
            store: new FakeStore(Approved(ImpersonationScope.ReadOnly)));

        await Run(context);

        Assert.True(NextWasCalled(context));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_read_only_session_may_not_write(string method)
    {
        var context = Ctx("/api/v1/admin/orders", method,
            store: new FakeStore(Approved(ImpersonationScope.ReadOnly)));

        await Run(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(ImpersonationGuardMiddleware.ReadOnlyMessage,
                     Message(context).GetProperty("responseMessage").GetString());
        // The refusal must mean NOTHING happened — not that it happened and was then reported.
        Assert.False(NextWasCalled(context));
    }

    // 3 ── a provider who explicitly granted write gets write.
    [Fact]
    public async Task A_read_write_session_may_write()
    {
        var context = Ctx("/api/v1/admin/orders", "POST",
            store: new FakeStore(Approved(ImpersonationScope.ReadWrite)));

        await Run(context);

        Assert.True(NextWasCalled(context));
    }

    // 4 ── THE ONE THAT MATTERS. The token is untouched and still valid; the provider said stop.
    //      Consent that cannot be withdrawn is not consent.
    [Theory]
    [InlineData(ImpersonationGrantStatus.Revoked)]
    [InlineData(ImpersonationGrantStatus.Expired)]
    [InlineData(ImpersonationGrantStatus.Pending)]   // never approved in the first place
    [InlineData(ImpersonationGrantStatus.Denied)]
    public async Task A_revoked_grant_stops_working_immediately(string status)
    {
        var context = Ctx("/api/v1/admin/orders", "GET",
            store: new FakeStore(new ImpersonationState(
                status, ImpersonationScope.ReadWrite, Brand, Support, DateTimeOffset.UtcNow.AddHours(1))));

        await Run(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(ImpersonationGuardMiddleware.NotActiveMessage,
                     Message(context).GetProperty("responseMessage").GetString());
        Assert.False(NextWasCalled(context));
    }

    // 5 ── a grant that no longer exists at all.
    [Fact]
    public async Task A_grant_that_has_vanished_is_refused()
    {
        var context = Ctx("/api/v1/admin/orders", "GET", store: new FakeStore(null));

        await Run(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(NextWasCalled(context));
    }

    // 6 ── the grant names ONE person and ONE brand. A token that has drifted from either — reissued
    //      under a different scope, or lifted by a colleague — is not the session consented to.
    [Fact]
    public async Task A_token_for_a_different_person_is_refused()
    {
        var context = Ctx("/api/v1/admin/orders", "GET",
            subject: Guid.NewGuid(),
            store: new FakeStore(Approved(ImpersonationScope.ReadWrite)));

        await Run(context);

        Assert.Equal(ImpersonationGuardMiddleware.MismatchMessage,
                     Message(context).GetProperty("responseMessage").GetString());
        Assert.False(NextWasCalled(context));
    }

    [Fact]
    public async Task A_token_pointed_at_a_different_brand_is_refused()
    {
        var context = Ctx("/api/v1/admin/orders", "GET",
            brand: Guid.NewGuid(),
            store: new FakeStore(Approved(ImpersonationScope.ReadWrite)));

        await Run(context);

        Assert.Equal(ImpersonationGuardMiddleware.MismatchMessage,
                     Message(context).GetProperty("responseMessage").GetString());
        Assert.False(NextWasCalled(context));
    }

    // 7 ── FAIL CLOSED, the deliberate inverse of BrandSuspensionMiddleware. Guessing wrong there
    //      takes healthy tenants offline; guessing wrong HERE leaves a stranger inside a customer's
    //      account with no provable consent. When we cannot tell, the answer is no.
    [Fact]
    public async Task A_store_that_throws_fails_closed()
    {
        var context = Ctx("/api/v1/admin/orders", "GET", store: new ThrowingStore());

        await Run(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(NextWasCalled(context));
    }

    [Fact]
    public async Task A_host_that_forgot_to_register_the_store_fails_closed()
    {
        var context = Ctx("/api/v1/admin/orders", "GET", store: null);

        await Run(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(NextWasCalled(context));
    }

    // 8 ── a token ASSERTING impersonation it cannot substantiate is not an ordinary session with
    //      junk in it. Refuse rather than fall through to the normal path.
    [Fact]
    public async Task A_malformed_grant_claim_is_refused_not_ignored()
    {
        var context = Ctx("/api/v1/admin/orders", "GET", rawGrant: "not-a-guid",
            store: new FakeStore(Approved(ImpersonationScope.ReadWrite)));

        await Run(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(NextWasCalled(context));
    }

    // 9 ── the DATABASE's scope wins over the token's. An owner downgrading a live session from
    //      write to read must not have to wait for a token to expire.
    [Fact]
    public async Task The_stores_scope_overrides_a_stale_claim()
    {
        var context = Ctx("/api/v1/admin/orders", "POST",
            claimScope: ImpersonationScope.ReadWrite,                     // what the token says
            store: new FakeStore(Approved(ImpersonationScope.ReadOnly))); // what is true now

        await Run(context);

        Assert.Equal(ImpersonationGuardMiddleware.ReadOnlyMessage,
                     Message(context).GetProperty("responseMessage").GetString());
        Assert.False(NextWasCalled(context));
    }

    // 10 ── the audit hand-off: a permitted request must carry the VALIDATED grant id forward, or
    //       every impersonated action lands in audit_logs indistinguishable from ordinary work.
    [Fact]
    public async Task A_permitted_request_hands_the_validated_grant_to_the_audit_stamper()
    {
        var context = Ctx("/api/v1/admin/orders", "GET",
            store: new FakeStore(Approved(ImpersonationScope.ReadOnly)));

        await Run(context);

        Assert.Equal(Grant, Assert.IsType<Guid>(context.Items[ImpersonationKeys.GrantIdItem]));
        Assert.Equal(ImpersonationScope.ReadOnly, context.Items[ImpersonationKeys.ScopeItem]);
    }

    [Fact]
    public async Task A_refused_request_hands_nothing_forward()
    {
        var context = Ctx("/api/v1/admin/orders", "POST",
            store: new FakeStore(Approved(ImpersonationScope.ReadOnly)));

        await Run(context);

        Assert.False(context.Items.ContainsKey(ImpersonationKeys.GrantIdItem));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private const string NextFlag = "next-was-called";

    private static ImpersonationState Approved(string scope) => new(
        ImpersonationGrantStatus.Approved, scope, Brand, Support, DateTimeOffset.UtcNow.AddHours(1));

    private static async Task Run(HttpContext context) =>
        await new ImpersonationGuardMiddleware(ctx =>
        {
            ctx.Items[NextFlag] = true;
            return Task.CompletedTask;
        }).InvokeAsync(context);

    private static bool NextWasCalled(HttpContext context) => context.Items.ContainsKey(NextFlag);

    private static DefaultHttpContext Ctx(
        string path, string method,
        IImpersonationStateStore? store = null,
        Guid? grant = null, string? rawGrant = null,
        Guid? subject = null, Guid? brand = null,
        string claimScope = ImpersonationScope.ReadOnly,
        bool omitGrantClaim = false)
    {
        var services = new ServiceCollection();
        if (store is not null) services.AddSingleton(store);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (subject ?? Support).ToString()),
            new("brand_id", (brand ?? Brand).ToString()),
        };

        // `grant: null` with no rawGrant means "an ordinary session" only when explicitly asked for;
        // the default is an impersonation token, because that is what these tests are about.
        var grantValue = rawGrant ?? (omitGrantClaim ? null : (grant ?? Grant).ToString());
        if (grantValue is not null)
        {
            claims.Add(new Claim(TokenClaims.ImpersonationGrantClaim, grantValue));
            claims.Add(new Claim(TokenClaims.ImpersonationScopeClaim, claimScope));
        }

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }

    private static JsonElement Message(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var doc = JsonDocument.Parse(context.Response.Body);
        return doc.RootElement.GetProperty("message").Clone();
    }

    private sealed class FakeStore(ImpersonationState? state) : IImpersonationStateStore
    {
        public int Calls { get; private set; }

        public Task<ImpersonationState?> GetAsync(Guid grantId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(state);
        }

        public Task<Guid?> RequestAsync(Guid brandId, Guid supportUserId, string reason, string scope,
            CancellationToken ct = default) => Task.FromResult<Guid?>(null);
    }

    private sealed class ThrowingStore : IImpersonationStateStore
    {
        public Task<ImpersonationState?> GetAsync(Guid grantId, CancellationToken ct = default)
            => throw new InvalidOperationException("database unreachable");

        public Task<Guid?> RequestAsync(Guid brandId, Guid supportUserId, string reason, string scope,
            CancellationToken ct = default) => throw new InvalidOperationException();
    }
}
