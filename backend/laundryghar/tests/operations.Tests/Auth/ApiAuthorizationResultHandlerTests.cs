using System.Security.Claims;
using System.Text.Json;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// <see cref="ApiAuthorizationResultHandler"/> turns an authorization denial into a machine-readable
/// answer. Two cases matter, and telling them apart is the point:
///
///   • <b>403 step_up_required</b> (§8) — you HAVE the permission and the plan; re-verify with an OTP.
///   • <b>402 feature_not_in_plan</b> (§5) — your BRAND has not bought the feature; upgrade.
///
/// They are indistinguishable from the token alone, because the entitlement filter removes
/// un-entitled permissions at mint — so a plan denial and a permission denial both arrive as a
/// missing permission. Answering 403 for both would tell a paying customer they lack permission when
/// what they lack is the plan, and give them nothing to act on.
/// </summary>
public class ApiAuthorizationResultHandlerTests
{
    // 1 ── §8: a step-up denial is a structured 403 the client can prompt + retry against.
    [Fact]
    public async Task Step_up_denial_is_rendered_as_structured_403()
    {
        var context = Ctx();
        var policy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();
        var reason = new StepUpRequiredFailureReason(new PermissionHandler(), "wallet.adjust");
        var result = PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([reason]));

        await new ApiAuthorizationResultHandler()
            .HandleAsync(next: _ => Task.CompletedTask, context, policy, result);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        var message = Message(context);
        Assert.Equal("step_up_required", message.GetProperty("responseMessage").GetString());
        var codes = message.GetProperty("errorMessage").GetProperty("step_up_required");
        Assert.Equal(1, codes.GetArrayLength());
        Assert.Equal("wallet.adjust", codes[0].GetString());
        Assert.Equal(context.Response.StatusCode, message.GetProperty("errorTypeCode").GetInt32());
    }

    // 2 ── §5: the denial was the PLAN's doing → 402 naming the feature and where to buy it.
    [Fact]
    public async Task Unlicensed_feature_denial_is_rendered_as_402_with_the_feature_and_upgrade_link()
    {
        // The brand does not own `custom_domain`, and `brands.update` sits behind it.
        var context = Ctx(unlicensed: "custom_domain api_access",
                          catalog: new FakeCatalog { ["brands.update"] = "custom_domain" });
        var policy = PermissionPolicy("brands.update");

        await new ApiAuthorizationResultHandler()
            .HandleAsync(next: _ => Task.CompletedTask, context, policy, PolicyAuthorizationResult.Forbid());

        Assert.Equal(StatusCodes.Status402PaymentRequired, context.Response.StatusCode);

        var message = Message(context);
        Assert.Equal("feature_not_in_plan", message.GetProperty("responseMessage").GetString());

        var detail = message.GetProperty("errorMessage").GetProperty("feature_not_in_plan");
        Assert.Equal("custom_domain", detail[0].GetString());              // WHAT is missing
        Assert.Equal(ApiAuthorizationResultHandler.UpgradePath, detail[1].GetString()); // WHERE to fix it

        // The body must AGREE with the wire. This shipped as 402-outside / 403-inside, found by
        // calling the live endpoint rather than by any test here: every assertion above passed
        // while the one field a client switches on said "permission problem" for a plan problem.
        Assert.Equal(context.Response.StatusCode, message.GetProperty("errorTypeCode").GetInt32());
    }

    // 3 ── the discrimination that matters: same shape of denial, but the feature IS owned, so this
    //      is a genuine permission problem and must stay 403. A 402 here would send someone to buy
    //      something they already have.
    [Fact]
    public async Task A_permission_denial_on_an_owned_feature_stays_403()
    {
        var context = Ctx(unlicensed: "custom_domain",          // owns everything except custom_domain
                          catalog: new FakeCatalog { ["orders.refund"] = "bookings" }); // bookings IS owned
        var policy = PermissionPolicy("orders.refund");

        await new ApiAuthorizationResultHandler()
            .HandleAsync(next: _ => Task.CompletedTask, context, policy, PolicyAuthorizationResult.Forbid());

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    // 4 ── step-up wins over entitlement. The caller holds the permission AND the plan and merely
    //      needs to re-verify; reporting "upgrade your plan" would be actively misleading.
    [Fact]
    public async Task Step_up_is_reported_even_when_the_brand_has_unlicensed_features()
    {
        var context = Ctx(unlicensed: "custom_domain",
                          catalog: new FakeCatalog { ["brands.update"] = "custom_domain" });
        var policy = PermissionPolicy("brands.update");
        var reason = new StepUpRequiredFailureReason(new PermissionHandler(), "brands.update");

        await new ApiAuthorizationResultHandler().HandleAsync(
            next: _ => Task.CompletedTask, context, policy,
            PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([reason])));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("step_up_required", Message(context).GetProperty("responseMessage").GetString());
    }

    // 5 ── with no ent_off claim (enforcement off, or the brand owns everything) the 402 path is
    //      never even consulted — today's shipped default must be untouched.
    [Fact]
    public async Task Without_the_entitlement_claim_nothing_becomes_402()
    {
        var catalog = new FakeCatalog { ["brands.update"] = "custom_domain" };
        var context = Ctx(catalog: catalog);
        var policy = PermissionPolicy("brands.update");

        await new ApiAuthorizationResultHandler()
            .HandleAsync(next: _ => Task.CompletedTask, context, policy, PolicyAuthorizationResult.Forbid());

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        // …and the catalogue is not consulted AT ALL. With no ent_off claim the answer is already
        // known, so the `unlicensed.Count > 0` guard must short-circuit before the lookup — otherwise
        // every ordinary 403 in the system pays for a feature-map query it cannot learn anything from.
        Assert.Equal(0, catalog.Lookups);
    }

    // 6 ── an ungated permission (orphan / core module → catalog returns null) is never a plan
    //      problem. Null must read as "entitlement has nothing to say", never as "not entitled".
    [Fact]
    public async Task An_ungated_permission_stays_403()
    {
        var context = Ctx(unlicensed: "custom_domain", catalog: new FakeCatalog()); // no mapping at all
        var policy = PermissionPolicy("dashboard.view");

        await new ApiAuthorizationResultHandler()
            .HandleAsync(next: _ => Task.CompletedTask, context, policy, PolicyAuthorizationResult.Forbid());

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static AuthorizationPolicy PermissionPolicy(string code) =>
        new AuthorizationPolicyBuilder().AddRequirements(new PermissionRequirement(code)).Build();

    private static DefaultHttpContext Ctx(string? unlicensed = null, IFeatureCatalog? catalog = null)
    {
        var claims = new List<Claim>();
        if (unlicensed is not null)
            claims.Add(new Claim(TokenClaims.EntitlementOffClaim, unlicensed));

        var services = new ServiceCollection();
        if (catalog is not null) services.AddSingleton(catalog);
        // The framework's default handler calls ForbidAsync, which needs an IAuthenticationService.
        // Registering a stub is what lets the "stays 403" cases actually exercise DELEGATION rather
        // than blow up before reaching it — the delegation IS the behaviour under test.
        services.AddSingleton<IAuthenticationService, StubAuthenticationService>();

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    private static JsonElement Message(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var doc = JsonDocument.Parse(context.Response.Body);
        return doc.RootElement.GetProperty("message").Clone();
    }

    /// <summary>Minimal IAuthenticationService so the framework default handler's ForbidAsync works
    /// offline. Records the 403 the way the real pipeline would.</summary>
    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        public Task SignInAsync(HttpContext c, string? s, ClaimsPrincipal p, AuthenticationProperties? a)
            => Task.CompletedTask;
        public Task SignOutAsync(HttpContext c, string? s, AuthenticationProperties? a)
            => Task.CompletedTask;
    }

    /// <summary>An in-memory permission → feature map, so the 402 decision is testable offline.</summary>
    private sealed class FakeCatalog : IFeatureCatalog
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
        public string this[string permission] { set => _map[permission] = value; }

        /// <summary>How many times the catalogue was consulted — lets a test prove the handler
        /// SKIPS the lookup when the token reports nothing missing.</summary>
        public int Lookups { get; private set; }

        public Task<string?> FeatureForPermissionAsync(string permissionCode, CancellationToken ct = default)
        {
            Lookups++;
            return Task.FromResult(_map.GetValueOrDefault(permissionCode));
        }
    }
}
