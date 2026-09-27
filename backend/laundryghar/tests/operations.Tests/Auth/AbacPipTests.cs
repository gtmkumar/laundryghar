using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Authorization.Abac;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the PIP (A2). The property under test throughout is the one the whole engine rests on:
/// a key that is ABSENT is unresolved and makes comparisons Indeterminate (→ deny), while a key
/// PRESENT with a null value is a known null and compares normally. Every fail-open in the audit
/// came from collapsing those two, so each resolver gets a test that says which it produces.
/// </summary>
public class AbacPipTests
{
    private static SubjectAttributeResolver SubjectResolver(
        string? tokenUse = "user", string? userType = "staff", string? permissions = "orders.read",
        string? scopeNodes = "", string? roles = "", Guid? subject = null, Guid? brandId = null)
        => new(RbacTestSupport.AccessorFor(RbacTestSupport.Principal(
            tokenUse: tokenUse, userType: userType, permissions: permissions,
            scopeNodes: scopeNodes, roles: roles,
            subject: subject ?? Guid.NewGuid(), brandId: brandId)));

    private static async Task<AbacAttributeBag> ResolveAsync(IAttributeResolver resolver)
    {
        var bag = AbacAttributeBag.Empty();
        await resolver.ResolveAsync(bag, CancellationToken.None);
        return bag;
    }

    // ── Subject resolver ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_principal_resolves_nothing()
    {
        // An anonymous caller must not produce a bag that any condition can be satisfied against.
        var resolver = new SubjectAttributeResolver(
            RbacTestSupport.AccessorFor(new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity())));   // no authenticationType → anonymous

        var bag = await ResolveAsync(resolver);

        Assert.Empty(bag.Values);
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(
            Compare(AbacAttributeKeys.SubjectUserType, ConditionOperator.Eq, "staff"), bag));
    }

    [Fact]
    public async Task Staff_token_resolves_customer_id_as_a_known_null_not_as_absent()
    {
        var staffId = Guid.NewGuid();
        var bag = await ResolveAsync(SubjectResolver(subject: staffId));

        Assert.True(bag.TryGet(AbacAttributeKeys.SubjectCustomerId, out var customerId));
        Assert.Null(customerId);                                   // resolved, and null
        Assert.Equal(staffId, bag[AbacAttributeKeys.SubjectUserId]);

        // Because it is RESOLVED, is_null answers rather than going indeterminate.
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare(AbacAttributeKeys.SubjectCustomerId, ConditionOperator.IsNull, null), bag));
    }

    [Fact]
    public async Task Customer_token_puts_sub_on_customer_id_and_leaves_user_id_null()
    {
        var customerId = Guid.NewGuid();
        var bag = await ResolveAsync(SubjectResolver(
            tokenUse: "customer", userType: null, subject: customerId));

        Assert.Equal(customerId, bag[AbacAttributeKeys.SubjectCustomerId]);
        Assert.Null(bag[AbacAttributeKeys.SubjectUserId]);
    }

    [Fact]
    public async Task Absent_scope_nodes_claim_is_omitted_so_within_scope_is_indeterminate()
    {
        // The A7.4 shape, expressed in the new engine: a token with no scope_nodes claim cannot be
        // evaluated against a scope rule, so the attribute is not supplied at all.
        var bag = await ResolveAsync(SubjectResolver(scopeNodes: null));

        Assert.False(bag.Has(AbacAttributeKeys.SubjectScopeNodes));

        var within = new AbacCondition
        {
            NodeType = ConditionNodeType.Compare,
            LeftAttribute = AbacAttributeKeys.SubjectScopeNodes,
            Operator = ConditionOperator.WithinScope,
        };
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(within, bag));
    }

    [Fact]
    public async Task Present_but_empty_scope_nodes_is_resolved_and_denies()
    {
        var bag = await ResolveAsync(SubjectResolver(scopeNodes: ""));

        Assert.True(bag.Has(AbacAttributeKeys.SubjectScopeNodes));

        var within = new AbacCondition
        {
            NodeType = ConditionNodeType.Compare,
            LeftAttribute = AbacAttributeKeys.SubjectScopeNodes,
            Operator = ConditionOperator.WithinScope,
        };
        // False, not Indeterminate: "you hold no memberships" is an answer.
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(within, bag));
    }

    [Fact]
    public async Task Roles_come_from_the_token_claim_and_an_absent_claim_stays_absent()
    {
        var withRoles = await ResolveAsync(SubjectResolver(roles: "auditor brand_admin"));
        Assert.Equal(
            new[] { "auditor", "brand_admin" },
            (string[])withRoles[AbacAttributeKeys.SubjectRoles]!);

        // No claim and no data source → absent → any deny over subject.roles fails closed.
        var withoutRoles = await ResolveAsync(SubjectResolver(roles: null));
        Assert.False(withoutRoles.Has(AbacAttributeKeys.SubjectRoles));
    }

    [Fact]
    public async Task Permission_codes_are_canonicalised_so_a_renamed_code_still_matches()
    {
        var bag = await ResolveAsync(SubjectResolver(permissions: "orders.read orders.create"));
        var perms = (string[])bag[AbacAttributeKeys.SubjectPermissions]!;

        Assert.Contains("orders.read", perms);
        Assert.All(perms, p => Assert.Equal(
            laundryghar.Utilities.Auth.PermissionAlias.Canonical(p), p));
    }

    // ── Environment resolver ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("user", "staff", EnvironmentAttributeResolver.Channel.Admin)]
    [InlineData("user", UserType.Rider, EnvironmentAttributeResolver.Channel.RiderApp)]
    [InlineData("customer", null, EnvironmentAttributeResolver.Channel.CustomerApp)]
    [InlineData("customer_mcp", null, EnvironmentAttributeResolver.Channel.CustomerApp)]
    [InlineData("partner", null, EnvironmentAttributeResolver.Channel.Api)]
    [InlineData("api_key", null, EnvironmentAttributeResolver.Channel.Api)]
    public async Task Channel_is_derived_from_the_signed_token_not_from_a_header(
        string tokenUse, string? userType, string expected)
    {
        var resolver = new EnvironmentAttributeResolver(
            RbacTestSupport.AccessorFor(RbacTestSupport.Principal(
                tokenUse: tokenUse, userType: userType)));

        var bag = await ResolveAsync(resolver);

        Assert.Equal(expected, bag[AbacAttributeKeys.EnvChannel]);
    }

    [Fact]
    public async Task Environment_supplies_a_clock_so_age_operators_are_reproducible()
    {
        var fixedNow = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(fixedNow);
        var resolver = new EnvironmentAttributeResolver(
            RbacTestSupport.AccessorFor(RbacTestSupport.Principal(tokenUse: "user")), clock);

        var bag = await ResolveAsync(resolver);

        Assert.Equal(fixedNow, bag[AbacAttributeKeys.EnvNow]);
    }

    // ── Provider composition ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resource_attributes_from_one_decision_do_not_leak_into_the_next()
    {
        // The provider memoises the subject/environment slice per request. If it handed out the
        // same bag instance each time, an order's brand_id would still be sitting in it when the
        // next decision asked about a payment — and a policy comparing resource.brand_id would
        // silently compare against the wrong row.
        var provider = new AbacAttributeProvider([SubjectResolver()], resourceResolver: null);

        var first = await provider.BuildAsync("orders", "read",
            new AbacResourceRef("orders", null, new Dictionary<string, object?>
            {
                [AbacAttributeKeys.ResourceBrandId] = Guid.NewGuid(),
            }), CancellationToken.None);

        var second = await provider.BuildAsync("payment", "refund", null, CancellationToken.None);

        Assert.True(first.Has(AbacAttributeKeys.ResourceBrandId));
        Assert.False(second.Has(AbacAttributeKeys.ResourceBrandId));
        Assert.Equal("refund", second[AbacAttributeKeys.ActionKey]);
    }

    [Fact]
    public async Task Known_attributes_are_used_verbatim_and_skip_the_resource_resolver()
    {
        var spy = new SpyResourceResolver();
        var provider = new AbacAttributeProvider([SubjectResolver()], spy);
        var brand = Guid.NewGuid();

        await provider.BuildAsync("orders", "read",
            new AbacResourceRef("orders", Guid.NewGuid(), new Dictionary<string, object?>
            {
                [AbacAttributeKeys.ResourceBrandId] = brand,
            }), CancellationToken.None);

        Assert.False(spy.WasCalled);   // the handler already had the row; no second query
    }

    [Fact]
    public async Task A_collection_question_supplies_no_resource_attributes()
    {
        // "May I list orders?" has no row, so resource.* stays absent and any policy comparing one
        // is Indeterminate. List endpoints must go through IPolicyFilterBuilder instead.
        var provider = new AbacAttributeProvider([SubjectResolver()], resourceResolver: null);

        var bag = await provider.BuildAsync("orders", "read", null, CancellationToken.None);

        Assert.False(bag.Has(AbacAttributeKeys.ResourceBrandId));
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(
            Compare(AbacAttributeKeys.ResourceBrandId, ConditionOperator.Eq, Guid.NewGuid().ToString()),
            bag));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static AbacCondition Compare(string left, string op, object? right) => new()
    {
        NodeType = ConditionNodeType.Compare,
        LeftAttribute = left,
        Operator = op,
        RightKind = "literal",
        RightValue = right,
    };

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SpyResourceResolver : IResourceAttributeResolver
    {
        public bool WasCalled { get; private set; }

        public ValueTask ResolveAsync(
            AbacAttributeBag bag, string resourceType, Guid resourceId, CancellationToken ct)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }
}
