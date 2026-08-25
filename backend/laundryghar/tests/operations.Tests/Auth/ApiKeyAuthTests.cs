using System.Security.Claims;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.Utilities.Auth.ApiKey;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// §11 P4 machine credentials (migration 0016).
///
/// <para>The tests are grouped by the three ways this feature could quietly fail: a malformed
/// credential reaching a lookup it should never reach, a scope granting more than it names, and a
/// human's session token satisfying a machine policy (or the reverse).</para>
/// </summary>
public class ApiKeyAuthTests
{
    // ── parsing ─────────────────────────────────────────────────────────────────────────────────
    // The parser is the outermost gate. Everything it accepts becomes a database lookup with
    // attacker-controlled input, so it accepts exactly one shape and nothing adjacent to it.

    private const string Handle = "3a6909c717228f30";   // 16 hex, the shape the issuer emits

    [Fact]
    public void A_well_formed_key_splits_into_a_public_handle_and_a_secret()
    {
        Assert.True(ApiKeyClaims.TryParse($"lg_live_{Handle}_SECRETVALUE", out var prefix, out var secret));

        // The prefix carries the environment, so a `test` key can never resolve to a `live` key that
        // happens to share a handle.
        Assert.Equal($"lg_live_{Handle}", prefix);
        Assert.Equal("SECRETVALUE", secret);
    }

    // THE ONE THAT WAS ACTUALLY BROKEN. Secrets are base64url, and that alphabet includes the
    // underscore — so a naive Split('_') returns five parts for a perfectly valid key and refuses
    // it. Every issued key containing an underscore in its secret 401'd, which on a 32-byte random
    // secret is most of them. Found by issuing a real key against a running host.
    [Theory]
    [InlineData("has_one_underscore")]
    [InlineData("_leading")]
    [InlineData("trailing_")]
    [InlineData("a_b_c_d_e_f")]
    [InlineData("Zm9vYmFy-_ABC123")]
    public void A_secret_containing_underscores_still_parses(string secretValue)
    {
        Assert.True(ApiKeyClaims.TryParse($"lg_live_{Handle}_{secretValue}", out var prefix, out var secret));

        Assert.Equal($"lg_live_{Handle}", prefix);
        Assert.Equal(secretValue, secret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("lg_live_3a6909c717228f30")]        // no secret
    [InlineData("lg_live_3a6909c717228f30_")]       // empty secret
    [InlineData("lg_live__SECRET")]                 // no handle
    [InlineData("lg_prod_3a6909c717228f30_SECRET")] // not an environment we issue
    [InlineData("xx_live_3a6909c717228f30_SECRET")] // not our vendor tag
    [InlineData("lg_live_a1_b2_SECRET")]            // handle is not 16 hex — must not re-join as one
    [InlineData("lg_live_3a6909c717228f3_SECRET")]  // 15 hex: one short
    [InlineData("lg_live_3a6909c717228f300_SECRET")]// 17 hex: one long
    [InlineData("lg_live_3A6909C717228F30_SECRET")] // uppercase: we never issue it
    [InlineData("lg_live_zzzzzzzzzzzzzzzz_SECRET")] // right length, not hex
    [InlineData("eyJhbGciOiJSUzI1NiJ9.abc")]        // an ordinary JWT on the same header
    public void Anything_that_is_not_exactly_our_shape_is_refused(string? candidate)
    {
        Assert.False(ApiKeyClaims.TryParse(candidate, out _, out _));
    }

    // ── scope enforcement ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_key_holding_the_scope_is_allowed()
    {
        Assert.True(await AllowsAsync("orders.read", KeyPrincipal("orders.read", "customers.read")));
    }

    [Fact]
    public async Task A_key_without_the_scope_is_refused()
    {
        Assert.False(await AllowsAsync("orders.write", KeyPrincipal("orders.read")));
    }

    // Exact matching only. `orders.read` must never satisfy `orders.readwrite`, and a scope must
    // never satisfy a longer one that merely starts with it — the kind of bug nobody finds until it
    // is the reason something was written to.
    [Theory]
    [InlineData("orders.read", "orders.readwrite")]
    [InlineData("orders", "orders.read")]
    [InlineData("orders.read", "orders")]
    [InlineData("ORDERS.READ", "orders.read")]   // and no case folding
    public async Task Scopes_never_match_by_prefix_or_case(string held, string required)
    {
        Assert.False(await AllowsAsync(required, KeyPrincipal(held)));
    }

    [Fact]
    public async Task The_wildcard_scope_grants_everything()
    {
        Assert.True(await AllowsAsync("anything.at.all", KeyPrincipal(ApiScopeHandler.Wildcard)));
    }

    [Fact]
    public async Task A_key_issued_no_scopes_can_do_nothing()
    {
        // The default from the create handler. It authenticates and is refused everywhere, which is
        // the safe outcome when someone clicked through a form without choosing.
        Assert.False(await AllowsAsync("orders.read", KeyPrincipal()));
    }

    // ── the two worlds stay separate ────────────────────────────────────────────────────────────

    // A signed-in human must never satisfy an API-key scope. If they could, the public API's scopes
    // would be advisory: any session token would open every scoped route.
    [Fact]
    public async Task A_signed_in_human_never_satisfies_an_api_scope()
    {
        var human = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("token_use", "user"),
                new Claim("permissions", "orders.read orders.create"),
                // even if a token somehow carried a scope claim
                new Claim(ApiKeyClaims.ScopeClaim, "orders.read"),
            ], "Bearer"));

        Assert.False(await AllowsAsync("orders.read", human));
    }

    [Fact]
    public async Task An_unauthenticated_caller_satisfies_nothing()
    {
        Assert.False(await AllowsAsync("orders.read", new ClaimsPrincipal(new ClaimsIdentity())));
    }

    // ── policy naming ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_policy_name_convention_builds_a_scope_requirement()
    {
        Assert.True(ApiScopePolicy.TryBuild("apiscope:orders.read", out var policy));
        var requirement = Assert.IsType<ApiScopeRequirement>(Assert.Single(policy!.Requirements));
        Assert.Equal("orders.read", requirement.Scope);

        // Bound to the ApiKey scheme, which is what stops a JWT from being evaluated against it.
        Assert.Equal([ApiKeyClaims.Scheme], policy.AuthenticationSchemes);
    }

    [Theory]
    [InlineData("permission:orders.read")]
    [InlineData("apiscope:")]
    [InlineData("CustomerOnly")]
    public void Other_policy_names_are_left_alone(string name)
    {
        Assert.False(ApiScopePolicy.TryBuild(name, out _));
    }

    // ── the record the store returns ────────────────────────────────────────────────────────────

    // Entitlement travels WITH the key rather than being checked at each call site, so it cannot be
    // forgotten in one. A brand that stopped paying for api_access has keys that stop working.
    [Fact]
    public void The_resolved_record_carries_the_brands_entitlement()
    {
        var record = new ApiKeyRecord(
            Guid.NewGuid(), Guid.NewGuid(), "hash", ["orders.read"], "active", "live",
            null, null, Entitled: false);

        Assert.False(record.Entitled);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal KeyPrincipal(params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new("token_use", ApiKeyClaims.TokenUse),
            new(ApiKeyClaims.KeyIdClaim, Guid.NewGuid().ToString()),
        };
        claims.AddRange(scopes.Select(s => new Claim(ApiKeyClaims.ScopeClaim, s)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyClaims.Scheme));
    }

    private static async Task<bool> AllowsAsync(string requiredScope, ClaimsPrincipal principal)
    {
        var requirement = new ApiScopeRequirement(requiredScope);
        var context = new AuthorizationHandlerContext([requirement], principal, resource: null);

        await new ApiScopeHandler().HandleAsync(context);
        return context.HasSucceeded;
    }
}
