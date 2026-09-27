using laundryghar.Utilities.Authorization.Abac;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the ABAC condition evaluator (A3.1) and the deny-overrides combining algorithm (A3.2)
/// from docs/ABAC_IMPLEMENTATION_PLAN.md.
///
/// The tests that matter most are the fail-closed ones. An authorization engine that treats "I
/// could not evaluate this" as "false" turns every unresolved attribute into a silent grant, which
/// is precisely the class of bug the existing code already has three instances of
/// (IsWithinScope's absent-claim allow, StepUp's absent-claim not-required, and the deny rows read
/// as grants by the two SQL propagation scripts). None of those may be reproduced here.
/// </summary>
public class AbacEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid BrandA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BrandB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Store1 = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly PolicyDecisionPoint Pdp = new();

    private static AbacCondition Compare(string attr, string op, object? right = null, string kind = "literal")
        => new() { NodeType = ConditionNodeType.Compare, LeftAttribute = attr, Operator = op, RightKind = kind, RightValue = right };

    private static AbacPolicy Policy(
        string key, string effect, AbacCondition? condition = null,
        int priority = 100, Guid? brandId = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, bool active = true)
        => new()
        {
            Key = key, Effect = effect, ResourceType = "commerce.payment", Action = "refund",
            Condition = condition, Priority = priority, BrandId = brandId,
            EffectiveFrom = from ?? DateTimeOffset.MinValue, EffectiveTo = to, IsActive = active,
        };

    private static AbacRequest Request(AbacAttributeBag? attrs = null, Guid? brand = null)
        => new("commerce.payment", "refund", attrs ?? AbacAttributeBag.Empty(), Now, brand ?? BrandA);

    // ── Combining algorithm ───────────────────────────────────────────────────────────────────

    [Fact]
    public void No_applicable_policy_is_not_applicable_and_therefore_not_allowed()
    {
        var decision = Pdp.Decide(Request(), []);

        Assert.Equal(AbacDecisionKind.NotApplicable, decision.Kind);
        Assert.False(decision.IsAllowed); // default-deny
    }

    [Fact]
    public void An_unconditional_permit_allows()
    {
        // This is the shape every one of the 771 existing role_permissions rows projects onto (A6.3):
        // a permit with no condition. ABAC must start behaviourally identical to RBAC.
        var decision = Pdp.Decide(Request(), [Policy("p", PolicyEffect.Permit)]);

        Assert.True(decision.IsAllowed);
        Assert.Equal("p", decision.MatchedPolicyKey);
    }

    [Fact]
    public void Deny_overrides_a_permit_regardless_of_priority()
    {
        var policies = new[]
        {
            Policy("permit-high-priority", PolicyEffect.Permit, priority: 1),
            Policy("deny-low-priority",    PolicyEffect.Deny,   priority: 999),
        };

        var decision = Pdp.Decide(Request(), policies);

        Assert.Equal(AbacDecisionKind.Deny, decision.Kind);
        Assert.Equal("deny-low-priority", decision.MatchedPolicyKey);
    }

    [Fact]
    public void A_deny_whose_condition_cannot_be_evaluated_still_denies()
    {
        // subject.roles is never resolved, so the condition is Indeterminate. Treating that as
        // "did not match" would drop the deny entirely — the fail-open bug.
        var deny = Policy("deny-unevaluable", PolicyEffect.Deny,
            Compare("subject.roles", ConditionOperator.Contains, "auditor"));

        var decision = Pdp.Decide(Request(), [Policy("permit", PolicyEffect.Permit), deny]);

        Assert.Equal(AbacDecisionKind.Deny, decision.Kind);
        Assert.Equal("deny-unevaluable", decision.MatchedPolicyKey);
        Assert.Contains("fails closed", decision.Reason);
    }

    [Fact]
    public void A_permit_whose_condition_cannot_be_evaluated_does_not_allow()
    {
        var permit = Policy("permit-unevaluable", PolicyEffect.Permit,
            Compare("resource.amount", ConditionOperator.Lt, 5000m));

        var decision = Pdp.Decide(Request(), [permit]); // resource.amount unresolved

        Assert.False(decision.IsAllowed);
        Assert.Equal(AbacDecisionKind.Deny, decision.Kind);
    }

    [Fact]
    public void Among_permits_the_lowest_priority_number_wins()
    {
        var attrs = new AbacAttributeBag().Set("resource.amount", 100m);
        var policies = new[]
        {
            Policy("second", PolicyEffect.Permit, Compare("resource.amount", ConditionOperator.Lt, 5000m), priority: 50),
            Policy("first",  PolicyEffect.Permit, Compare("resource.amount", ConditionOperator.Lt, 9000m), priority: 10),
        };

        var decision = Pdp.Decide(Request(attrs), policies);

        Assert.True(decision.IsAllowed);
        Assert.Equal("first", decision.MatchedPolicyKey);
    }

    // ── Date-versioning and tenancy ───────────────────────────────────────────────────────────

    [Fact]
    public void A_policy_outside_its_effective_window_does_not_apply()
    {
        var expired = Policy("expired", PolicyEffect.Permit, to: Now.AddDays(-1));
        var future  = Policy("future",  PolicyEffect.Permit, from: Now.AddDays(1));

        Assert.False(Pdp.Decide(Request(), [expired]).IsAllowed);
        Assert.False(Pdp.Decide(Request(), [future]).IsAllowed);
        Assert.False(Pdp.Decide(Request(), [Policy("inactive", PolicyEffect.Permit, active: false)]).IsAllowed);
    }

    [Fact]
    public void A_platform_policy_applies_to_every_brand_but_a_brand_policy_only_to_its_own()
    {
        var platform = Policy("platform-rule", PolicyEffect.Permit, brandId: null);
        var brandBOnly = Policy("brand-b-rule", PolicyEffect.Permit, brandId: BrandB);

        // Platform-authored: applies to a Brand-A caller. This is the arm identity_access.roles
        // omits, which is why a brand admin there cannot SELECT a single system role.
        Assert.True(Pdp.Decide(Request(brand: BrandA), [platform]).IsAllowed);

        // Brand-B-authored: must NOT apply to a Brand-A caller.
        Assert.False(Pdp.Decide(Request(brand: BrandA), [brandBOnly]).IsAllowed);
        Assert.True(Pdp.Decide(Request(brand: BrandB), [brandBOnly]).IsAllowed);
    }

    // ── Three-valued boolean logic ────────────────────────────────────────────────────────────

    [Fact]
    public void And_short_circuits_on_false_but_propagates_indeterminate()
    {
        var attrs = new AbacAttributeBag().Set("resource.status", "captured");

        // false AND unresolved → False (the false settles it; no need to resolve the other side)
        var falseAnd = new AbacCondition
        {
            NodeType = ConditionNodeType.And,
            Children =
            [
                Compare("resource.status", ConditionOperator.Eq, "refunded"),
                Compare("resource.amount", ConditionOperator.Lt, 100m),
            ],
        };
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(falseAnd, attrs));

        // true AND unresolved → Indeterminate
        var indeterminateAnd = new AbacCondition
        {
            NodeType = ConditionNodeType.And,
            Children =
            [
                Compare("resource.status", ConditionOperator.Eq, "captured"),
                Compare("resource.amount", ConditionOperator.Lt, 100m),
            ],
        };
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(indeterminateAnd, attrs));
    }

    [Fact]
    public void Or_short_circuits_on_true_but_propagates_indeterminate()
    {
        var attrs = new AbacAttributeBag().Set("resource.status", "captured");

        var trueOr = new AbacCondition
        {
            NodeType = ConditionNodeType.Or,
            Children =
            [
                Compare("resource.status", ConditionOperator.Eq, "captured"),
                Compare("resource.amount", ConditionOperator.Lt, 100m),
            ],
        };
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(trueOr, attrs));

        var indeterminateOr = new AbacCondition
        {
            NodeType = ConditionNodeType.Or,
            Children =
            [
                Compare("resource.status", ConditionOperator.Eq, "refunded"),
                Compare("resource.amount", ConditionOperator.Lt, 100m),
            ],
        };
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(indeterminateOr, attrs));
    }

    [Fact]
    public void Not_inverts_true_and_false_but_never_indeterminate()
    {
        var attrs = new AbacAttributeBag().Set("resource.status", "captured");

        AbacCondition Not(AbacCondition inner) =>
            new() { NodeType = ConditionNodeType.Not, Children = [inner] };

        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(
            Not(Compare("resource.status", ConditionOperator.Eq, "captured")), attrs));
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Not(Compare("resource.status", ConditionOperator.Eq, "refunded")), attrs));
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(
            Not(Compare("resource.amount", ConditionOperator.Lt, 1m)), attrs));
    }

    [Fact]
    public void A_malformed_node_is_indeterminate_not_true()
    {
        var attrs = AbacAttributeBag.Empty();

        // NOT with two children is malformed; an unknown node type is unrecognised. Both must fail
        // closed rather than default to allow.
        var badNot = new AbacCondition
        {
            NodeType = ConditionNodeType.Not,
            Children = [Compare("a", ConditionOperator.IsNull), Compare("b", ConditionOperator.IsNull)],
        };
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(badNot, attrs));
        Assert.Equal(Ternary.Indeterminate,
            ConditionEvaluator.Evaluate(new AbacCondition { NodeType = "xor" }, attrs));
    }

    // ── Operators ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Equality_normalises_across_the_types_a_jsonb_literal_can_arrive_as()
    {
        // The literal comes out of jsonb as a string; the attribute is a real Guid/decimal.
        var attrs = new AbacAttributeBag()
            .Set("resource.brand_id", BrandA)
            .Set("resource.amount", 250m);

        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.brand_id", ConditionOperator.Eq, BrandA.ToString()), attrs));
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.amount", ConditionOperator.Eq, "250"), attrs));
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(
            Compare("resource.brand_id", ConditionOperator.Eq, BrandB.ToString()), attrs));
    }

    [Fact]
    public void In_contains_and_starts_with_behave_as_documented()
    {
        var attrs = new AbacAttributeBag()
            .Set("resource.status", "captured")
            .Set("subject.roles", new[] { "store_admin", "support" });

        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.status", ConditionOperator.In, new[] { "captured", "completed" }), attrs));
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.status", ConditionOperator.NotIn, new[] { "refunded" }), attrs));

        // contains over a LIST attribute is membership …
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("subject.roles", ConditionOperator.Contains, "support"), attrs));
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(
            Compare("subject.roles", ConditionOperator.Contains, "auditor"), attrs));

        // … and over a TEXT attribute is substring.
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.status", ConditionOperator.Contains, "captur"), attrs));
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.status", ConditionOperator.StartsWith, "cap"), attrs));
    }

    [Fact]
    public void Ordering_comparisons_refuse_incomparable_types()
    {
        var attrs = new AbacAttributeBag()
            .Set("resource.amount", 4999m)
            .Set("resource.status", "captured");

        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.amount", ConditionOperator.Lt, 5000m), attrs));
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(
            Compare("resource.amount", ConditionOperator.Gt, 5000m), attrs));

        // Ordering two strings is not meaningful for an authorization decision — fail closed.
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(
            Compare("resource.status", ConditionOperator.Gt, "aardvark"), attrs));
    }

    [Fact]
    public void Is_null_distinguishes_a_resolved_null_from_an_unresolved_attribute()
    {
        var attrs = new AbacAttributeBag().Set("resource.owner_user_id", null);

        // Resolved to null → is_null is genuinely true.
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.owner_user_id", ConditionOperator.IsNull), attrs));

        // Never resolved → we do not know, so Indeterminate rather than true.
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(
            Compare("resource.store_id", ConditionOperator.IsNull), attrs));
    }

    [Fact]
    public void Time_window_operators_use_the_supplied_clock_and_refuse_to_guess_without_one()
    {
        var withClock = new AbacAttributeBag()
            .Set(AbacAttributeKeys.EnvNow, Now)
            .Set("resource.created_at", Now.AddMinutes(-10));

        // Created 10 minutes ago: older than 5 minutes (300s), not newer than it.
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.created_at", ConditionOperator.OlderThan, 300), withClock));
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(
            Compare("resource.created_at", ConditionOperator.NewerThan, 300), withClock));

        // Within the last hour.
        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.created_at", ConditionOperator.NewerThan, 3600), withClock));

        // No env.now supplied → the engine must not fall back to the ambient clock, because a
        // replayed decision would then not be reproducible.
        var noClock = new AbacAttributeBag().Set("resource.created_at", Now.AddMinutes(-10));
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(
            Compare("resource.created_at", ConditionOperator.OlderThan, 300), noClock));
    }

    [Fact]
    public void Right_kind_attribute_compares_two_attributes()
    {
        var attrs = new AbacAttributeBag()
            .Set("subject.brand_id", BrandA)
            .Set("resource.brand_id", BrandA);

        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("resource.brand_id", ConditionOperator.Eq, "subject.brand_id", kind: "attribute"), attrs));

        attrs.Set("resource.brand_id", BrandB);
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(
            Compare("resource.brand_id", ConditionOperator.Eq, "subject.brand_id", kind: "attribute"), attrs));

        // Comparand attribute missing → Indeterminate, not false.
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(
            Compare("resource.brand_id", ConditionOperator.Eq, "subject.store_id", kind: "attribute"), attrs));
    }

    // ── within_scope: the operator form of the 95 IsWithinScope call sites ────────────────────

    [Fact]
    public void Within_scope_matches_a_platform_node_against_anything()
    {
        var attrs = new AbacAttributeBag()
            .Set(AbacAttributeKeys.SubjectScopeNodes, new[] { "platform" })
            .Set(AbacAttributeKeys.ResourceBrandId, BrandB);

        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("subject.scope_nodes", ConditionOperator.WithinScope), attrs));
    }

    [Fact]
    public void Within_scope_matches_at_the_right_level_and_not_across_levels()
    {
        var attrs = new AbacAttributeBag()
            .Set(AbacAttributeKeys.SubjectScopeNodes, new[] { $"store:{Store1}" })
            .Set(AbacAttributeKeys.ResourceBrandId, BrandA)
            .Set(AbacAttributeKeys.ResourceStoreId, Store1);

        Assert.Equal(Ternary.True, ConditionEvaluator.Evaluate(
            Compare("subject.scope_nodes", ConditionOperator.WithinScope), attrs));

        // Same store id, but the resource is at a different store → no match.
        attrs.Set(AbacAttributeKeys.ResourceStoreId, Guid.NewGuid());
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(
            Compare("subject.scope_nodes", ConditionOperator.WithinScope), attrs));
    }

    [Fact]
    public void Within_scope_denies_on_empty_nodes_and_is_indeterminate_when_the_claim_is_absent()
    {
        // Present but empty = a membership-less principal = deny. This half matches IsWithinScope.
        var empty = new AbacAttributeBag()
            .Set(AbacAttributeKeys.SubjectScopeNodes, Array.Empty<string>())
            .Set(AbacAttributeKeys.ResourceBrandId, BrandA);
        Assert.Equal(Ternary.False, ConditionEvaluator.Evaluate(
            Compare("subject.scope_nodes", ConditionOperator.WithinScope), empty));

        // ABSENT is where this deliberately diverges: HttpContextCurrentUser.cs:84 returns TRUE for a
        // missing scope_nodes claim (rollout safety for pre-feature tokens), so a stripped claim
        // allows everything. The ABAC operator fails closed instead. Task A7.4 retires the old
        // behaviour once no pre-feature tokens remain.
        var absent = new AbacAttributeBag().Set(AbacAttributeKeys.ResourceBrandId, BrandA);
        Assert.Equal(Ternary.Indeterminate, ConditionEvaluator.Evaluate(
            Compare("subject.scope_nodes", ConditionOperator.WithinScope), absent));
    }

    // ── The worked example from the plan ──────────────────────────────────────────────────────

    [Fact]
    public void Refund_ceiling_by_role_the_rule_that_is_not_expressible_today()
    {
        // "A store_admin may refund up to ₹5,000; above that needs a finance_manager." Today this
        // costs a C# edit in IssueRefundHandler — there is no per-role refund ceiling anywhere in
        // the system. Here it is two rows.
        var ceiling = new AbacCondition
        {
            NodeType = ConditionNodeType.And,
            Children =
            [
                Compare("subject.roles", ConditionOperator.Contains, "store_admin"),
                Compare("resource.amount", ConditionOperator.Gt, 5000m),
                Compare("resource.status", ConditionOperator.In, new[] { "captured", "completed" }),
            ],
        };

        var policies = new[]
        {
            Policy("refund.baseline", PolicyEffect.Permit,
                Compare("resource.status", ConditionOperator.In, new[] { "captured", "completed" })),
            Policy("refund.store-admin-ceiling", PolicyEffect.Deny, ceiling),
        };

        AbacAttributeBag Bag(decimal amount, string role) => new AbacAttributeBag()
            .Set("subject.roles", new[] { role })
            .Set("resource.amount", amount)
            .Set("resource.status", "captured");

        // Under the ceiling → allowed.
        Assert.True(Pdp.Decide(Request(Bag(4_999m, "store_admin")), policies).IsAllowed);

        // Over it → denied, and the denial names the rule that produced it (A3.4 explain).
        var over = Pdp.Decide(Request(Bag(5_001m, "store_admin")), policies);
        Assert.False(over.IsAllowed);
        Assert.Equal("refund.store-admin-ceiling", over.MatchedPolicyKey);

        // A finance_manager is unaffected by a ceiling written for store_admin.
        Assert.True(Pdp.Decide(Request(Bag(50_000m, "finance_manager")), policies).IsAllowed);

        // A payment in the wrong status is denied by the baseline not matching at all.
        var wrongStatus = new AbacAttributeBag()
            .Set("subject.roles", new[] { "finance_manager" })
            .Set("resource.amount", 10m)
            .Set("resource.status", "refunded");
        Assert.False(Pdp.Decide(Request(wrongStatus), policies).IsAllowed);
    }
}
