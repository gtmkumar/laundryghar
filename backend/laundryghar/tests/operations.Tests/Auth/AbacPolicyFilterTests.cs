using laundryghar.Utilities.Authorization.Abac;
using Microsoft.Extensions.Options;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the list-filter compiler (A4.3). The rule under test everywhere: the predicate this
/// builds must never be WIDER than the decision the PDP would make row by row. A list that shows a
/// row the detail endpoint then refuses to open is a bug; a list that shows a row nobody should see
/// is a breach. So an untranslatable PERMIT contributes nothing, and an untranslatable DENY hides
/// everything.
/// </summary>
public class AbacPolicyFilterTests
{
    private sealed class Row
    {
        public Guid Id { get; init; }
        public Guid? BrandId { get; init; }
        public Guid? FranchiseId { get; init; }
        public Guid? StoreId { get; init; }
        public string? Status { get; init; }
        public decimal Amount { get; init; }
    }

    private static readonly Dictionary<string, string> Map = new()
    {
        [AbacAttributeKeys.ResourceId]          = nameof(Row.Id),
        [AbacAttributeKeys.ResourceBrandId]     = nameof(Row.BrandId),
        [AbacAttributeKeys.ResourceFranchiseId] = nameof(Row.FranchiseId),
        [AbacAttributeKeys.ResourceStoreId]     = nameof(Row.StoreId),
        [AbacAttributeKeys.ResourceStatus]      = nameof(Row.Status),
        [AbacAttributeKeys.ResourceAmount]      = nameof(Row.Amount),
    };

    private static async Task<Func<Row, bool>> BuildAsync(
        IReadOnlyList<AbacPolicy> policies, AbacAttributeBag bag, bool enforce = true)
    {
        var options = Options.Create(new AbacOptions
        {
            Enabled = true,
            Mode = enforce ? AbacMode.Enforce : AbacMode.Shadow,
        });

        var builder = new PolicyFilterBuilder(
            new StubRepository(policies), new StubProvider(bag), options);

        var expression = await builder.BuildAsync<Row>("orders", "read", Map, CancellationToken.None);
        return expression.Compile();
    }

    private static AbacPolicy Permit(string key, AbacCondition? condition, int priority = 100)
        => new()
        {
            Key = key, Effect = PolicyEffect.Permit, ResourceType = "orders", Action = "read",
            Priority = priority, Condition = condition,
        };

    private static AbacPolicy Deny(string key, AbacCondition? condition, int priority = 50)
        => new()
        {
            Key = key, Effect = PolicyEffect.Deny, ResourceType = "orders", Action = "read",
            Priority = priority, Condition = condition,
        };

    private static AbacCondition Compare(string left, string op, object? right, string kind = "literal")
        => new()
        {
            NodeType = ConditionNodeType.Compare,
            LeftAttribute = left, Operator = op, RightKind = kind, RightValue = right,
        };

    private static AbacAttributeBag Bag(params (string Key, object? Value)[] values)
    {
        var bag = AbacAttributeBag.Empty();
        bag.Set(AbacAttributeKeys.EnvNow, DateTimeOffset.UtcNow);
        foreach (var (key, value) in values) bag.Set(key, value);
        return bag;
    }

    // ── Default-deny ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_policies_means_no_rows()
    {
        var predicate = await BuildAsync([], Bag());

        Assert.False(predicate(new Row { BrandId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Shadow_mode_leaves_the_callers_own_filtering_untouched()
    {
        // In shadow the builder must not narrow anything, or a list would silently shrink before
        // anyone has reviewed a single parity report.
        var predicate = await BuildAsync([], Bag(), enforce: false);

        Assert.True(predicate(new Row { BrandId = Guid.NewGuid() }));
    }

    // ── Brand equality, the common shape ──────────────────────────────────────────────────────

    [Fact]
    public async Task Row_brand_must_equal_subject_brand()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        var predicate = await BuildAsync(
            [Permit("brand", Compare(
                AbacAttributeKeys.ResourceBrandId, ConditionOperator.Eq,
                AbacAttributeKeys.SubjectBrandId, kind: "attribute"))],
            Bag((AbacAttributeKeys.SubjectBrandId, mine)));

        Assert.True(predicate(new Row { BrandId = mine }));
        Assert.False(predicate(new Row { BrandId = theirs }));
        Assert.False(predicate(new Row { BrandId = null }));
    }

    // ── within_scope ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Within_scope_matches_the_level_the_membership_is_at()
    {
        var brand = Guid.NewGuid();
        var franchise = Guid.NewGuid();
        var otherFranchise = Guid.NewGuid();

        var predicate = await BuildAsync(
            [Permit("scope", new AbacCondition
            {
                NodeType = ConditionNodeType.Compare,
                LeftAttribute = AbacAttributeKeys.SubjectScopeNodes,
                Operator = ConditionOperator.WithinScope,
            })],
            Bag((AbacAttributeKeys.SubjectScopeNodes, new[] { $"franchise:{franchise}" })));

        Assert.True(predicate(new Row { BrandId = brand, FranchiseId = franchise }));
        Assert.False(predicate(new Row { BrandId = brand, FranchiseId = otherFranchise }));
        Assert.False(predicate(new Row { BrandId = brand, FranchiseId = null }));
    }

    [Fact]
    public async Task A_platform_node_matches_every_row()
    {
        var predicate = await BuildAsync(
            [Permit("scope", new AbacCondition
            {
                NodeType = ConditionNodeType.Compare,
                LeftAttribute = AbacAttributeKeys.SubjectScopeNodes,
                Operator = ConditionOperator.WithinScope,
            })],
            Bag((AbacAttributeKeys.SubjectScopeNodes, new[] { "platform" })));

        Assert.True(predicate(new Row { BrandId = Guid.NewGuid(), FranchiseId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Absent_scope_nodes_yields_no_rows_rather_than_all_rows()
    {
        // The list-layer twin of the A7.4 fix. An unresolvable permit must not fall back to "true".
        var predicate = await BuildAsync(
            [Permit("scope", new AbacCondition
            {
                NodeType = ConditionNodeType.Compare,
                LeftAttribute = AbacAttributeKeys.SubjectScopeNodes,
                Operator = ConditionOperator.WithinScope,
            })],
            Bag());   // no scope_nodes at all

        Assert.False(predicate(new Row { BrandId = Guid.NewGuid() }));
    }

    // ── Deny-overrides ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_deny_subtracts_from_a_matching_permit()
    {
        var brand = Guid.NewGuid();

        var predicate = await BuildAsync(
            [
                Permit("all-in-brand", Compare(
                    AbacAttributeKeys.ResourceBrandId, ConditionOperator.Eq,
                    AbacAttributeKeys.SubjectBrandId, kind: "attribute")),
                Deny("not-cancelled", Compare(
                    AbacAttributeKeys.ResourceStatus, ConditionOperator.Eq, "cancelled")),
            ],
            Bag((AbacAttributeKeys.SubjectBrandId, brand)));

        Assert.True(predicate(new Row { BrandId = brand, Status = "delivered" }));
        Assert.False(predicate(new Row { BrandId = brand, Status = "cancelled" }));
    }

    [Fact]
    public async Task An_untranslatable_deny_hides_everything()
    {
        var brand = Guid.NewGuid();

        // The deny references a subject attribute that was never resolved, so it cannot be expressed
        // as SQL. The PDP would treat that deny as Indeterminate and deny; the filter must agree,
        // or the list would show rows the detail endpoint refuses.
        var predicate = await BuildAsync(
            [
                Permit("all-in-brand", Compare(
                    AbacAttributeKeys.ResourceBrandId, ConditionOperator.Eq,
                    AbacAttributeKeys.SubjectBrandId, kind: "attribute")),
                Deny("unresolvable", Compare(
                    AbacAttributeKeys.ResourceStatus, ConditionOperator.Eq,
                    "subject.nonexistent", kind: "attribute")),
            ],
            Bag((AbacAttributeKeys.SubjectBrandId, brand)));

        Assert.False(predicate(new Row { BrandId = brand, Status = "delivered" }));
    }

    [Fact]
    public async Task An_untranslatable_permit_simply_does_not_widen()
    {
        var brand = Guid.NewGuid();

        var predicate = await BuildAsync(
            [
                Permit("unresolvable", Compare(
                    AbacAttributeKeys.ResourceStatus, ConditionOperator.Eq,
                    "subject.nonexistent", kind: "attribute")),
                Permit("brand", Compare(
                    AbacAttributeKeys.ResourceBrandId, ConditionOperator.Eq,
                    AbacAttributeKeys.SubjectBrandId, kind: "attribute")),
            ],
            Bag((AbacAttributeKeys.SubjectBrandId, brand)));

        Assert.True(predicate(new Row { BrandId = brand }));
        Assert.False(predicate(new Row { BrandId = Guid.NewGuid() }));
    }

    // ── Boolean shapes ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_empty_OR_is_false_and_an_empty_AND_is_true()
    {
        var brand = Guid.NewGuid();

        var orPredicate = await BuildAsync(
            [Permit("empty-or", new AbacCondition { NodeType = ConditionNodeType.Or })], Bag());
        Assert.False(orPredicate(new Row { BrandId = brand }));

        var andPredicate = await BuildAsync(
            [Permit("empty-and", new AbacCondition { NodeType = ConditionNodeType.And })], Bag());
        Assert.True(andPredicate(new Row { BrandId = brand }));
    }

    [Fact]
    public async Task A_null_condition_is_an_unconditional_permit()
    {
        var predicate = await BuildAsync([Permit("open", null)], Bag());

        Assert.True(predicate(new Row { BrandId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Numeric_thresholds_compile_against_a_non_nullable_column()
    {
        var predicate = await BuildAsync(
            [Permit("under-5000", Compare(
                AbacAttributeKeys.ResourceAmount, ConditionOperator.Lte, 5000))],
            Bag());

        Assert.True(predicate(new Row { Amount = 4999m }));
        Assert.True(predicate(new Row { Amount = 5000m }));
        Assert.False(predicate(new Row { Amount = 5001m }));
    }

    [Fact]
    public async Task In_over_a_literal_list_becomes_an_or_chain()
    {
        var predicate = await BuildAsync(
            [Permit("open-states", Compare(
                AbacAttributeKeys.ResourceStatus, ConditionOperator.In,
                new List<object?> { "placed", "picked_up" }))],
            Bag());

        Assert.True(predicate(new Row { Status = "placed" }));
        Assert.True(predicate(new Row { Status = "picked_up" }));
        Assert.False(predicate(new Row { Status = "delivered" }));
    }

    [Fact]
    public async Task A_brand_policy_belonging_to_another_brand_is_ignored()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        var foreignPolicy = new AbacPolicy
        {
            Key = "theirs", Effect = PolicyEffect.Permit, ResourceType = "orders", Action = "read",
            BrandId = theirs, Condition = null,
        };

        var predicate = await BuildAsync([foreignPolicy], Bag((AbacAttributeKeys.SubjectBrandId, mine)));

        Assert.False(predicate(new Row { BrandId = mine }));
    }

    // ── stubs ─────────────────────────────────────────────────────────────────────────────────

    private sealed class StubRepository(IReadOnlyList<AbacPolicy> policies) : IPolicyRepository
    {
        public Task<IReadOnlyList<AbacPolicy>> GetAsync(string resourceType, string action, CancellationToken ct)
            => Task.FromResult(policies);

        public void Invalidate() { }
    }

    private sealed class StubProvider(AbacAttributeBag bag) : IAbacAttributeProvider
    {
        public ValueTask<AbacAttributeBag> BuildAsync(
            string resourceType, string action, AbacResourceRef? resource, CancellationToken ct)
            => ValueTask.FromResult(bag);
    }
}
