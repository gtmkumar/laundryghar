namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>Policy effect. Mirrors authz.policy.effect.</summary>
public static class PolicyEffect
{
    public const string Permit = "permit";
    public const string Deny   = "deny";
}

/// <summary>Condition tree node kinds. Mirrors authz.policy_condition.node_type.</summary>
public static class ConditionNodeType
{
    public const string And     = "and";
    public const string Or      = "or";
    public const string Not     = "not";
    public const string Compare = "compare";
}

/// <summary>Comparison operators. Mirrors the CHECK on authz.policy_condition.operator.</summary>
public static class ConditionOperator
{
    public const string Eq          = "eq";
    public const string Neq         = "neq";
    public const string Lt          = "lt";
    public const string Lte         = "lte";
    public const string Gt          = "gt";
    public const string Gte         = "gte";
    public const string In          = "in";
    public const string NotIn       = "not_in";
    public const string Contains    = "contains";
    public const string StartsWith  = "starts_with";
    public const string IsNull      = "is_null";
    public const string IsNotNull   = "is_not_null";
    /// <summary>The §6 ancestor-or-self check, evaluated from subject.scope_nodes against the
    /// resource's brand/franchise/store/warehouse ids. The operator form of the 95 hand-written
    /// ICurrentUser.IsWithinScope call sites this layer replaces (A4.4).</summary>
    public const string WithinScope = "within_scope";
    /// <summary>left (a timestamp) is strictly older than `now - right` seconds.</summary>
    public const string OlderThan   = "older_than";
    /// <summary>left (a timestamp) is within the last `right` seconds.</summary>
    public const string NewerThan   = "newer_than";
}

/// <summary>
/// The attribute vocabulary. Every constant here has a matching row in <c>authz.attribute</c>
/// (migration 0024 §8) — a policy may not reference a key that is not registered there, so these
/// two lists are one list kept in two places and the CI check in
/// <c>AbacAttributeCatalogueTests</c> fails the build when they drift.
/// </summary>
public static class AbacAttributeKeys
{
    // ── subject (resolver_key = "subject") ────────────────────────────────────────────────────
    public const string SubjectUserId        = "subject.user_id";
    public const string SubjectCustomerId    = "subject.customer_id";
    public const string SubjectUserType      = "subject.user_type";
    public const string SubjectTokenUse      = "subject.token_use";
    public const string SubjectBrandId       = "subject.brand_id";
    public const string SubjectFranchiseId   = "subject.franchise_id";
    public const string SubjectStoreId       = "subject.store_id";
    public const string SubjectPartnerId     = "subject.partner_id";
    public const string SubjectRoles         = "subject.roles";
    public const string SubjectPermissions   = "subject.permissions";
    public const string SubjectScopeNodes    = "subject.scope_nodes";
    public const string SubjectStepUpAt      = "subject.stepup_at";
    public const string SubjectEntitlements  = "subject.entitlements";

    // ── resource (resolver_key = NULL; read through resource_type.attribute_map) ───────────────
    public const string ResourceId           = "resource.id";
    public const string ResourceBrandId      = "resource.brand_id";
    public const string ResourceFranchiseId  = "resource.franchise_id";
    public const string ResourceStoreId      = "resource.store_id";
    public const string ResourceWarehouseId  = "resource.warehouse_id";
    public const string ResourceCustomerId   = "resource.customer_id";
    public const string ResourceOwnerUserId  = "resource.owner_user_id";
    public const string ResourceStatus       = "resource.status";
    public const string ResourceAmount       = "resource.amount";
    public const string ResourceCreatedAt    = "resource.created_at";

    // ── action ────────────────────────────────────────────────────────────────────────────────
    public const string ActionKey            = "action.key";

    // ── environment (resolver_key = "environment") ────────────────────────────────────────────
    public const string EnvNow               = "env.now";
    public const string EnvIp                = "env.ip";
    public const string EnvChannel           = "env.channel";

    /// <summary>Not an authz.attribute row — an internal marker the PDP uses to echo the resource
    /// type back into the decision log. Never referenceable from a policy condition.</summary>
    public const string ResourceTypeKey      = "__resource_type";

    /// <summary>Every key that must exist in authz.attribute, in catalogue order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        SubjectUserId, SubjectCustomerId, SubjectUserType, SubjectTokenUse, SubjectBrandId,
        SubjectFranchiseId, SubjectStoreId, SubjectPartnerId, SubjectRoles, SubjectPermissions,
        SubjectScopeNodes, SubjectStepUpAt, SubjectEntitlements,
        ResourceId, ResourceBrandId, ResourceFranchiseId, ResourceStoreId, ResourceWarehouseId,
        ResourceCustomerId, ResourceOwnerUserId, ResourceStatus, ResourceAmount, ResourceCreatedAt,
        ActionKey,
        EnvNow, EnvIp, EnvChannel,
    ];
}

/// <summary>
/// Three-valued evaluation result. The third value is what makes the engine fail closed: an
/// attribute the PIP could not resolve yields <see cref="Indeterminate"/> rather than false, so a
/// DENY policy whose condition cannot be evaluated still denies instead of being silently skipped.
/// </summary>
public enum Ternary
{
    False,
    True,
    Indeterminate,
}

/// <summary>One node of a policy's condition tree. Hydrated from authz.policy_condition.</summary>
public sealed class AbacCondition
{
    public required string NodeType { get; init; }
    public string? LeftAttribute { get; init; }
    public string? Operator { get; init; }
    /// <summary>literal | attribute | function.</summary>
    public string? RightKind { get; init; }
    /// <summary>For RightKind=literal this is the value (or a list, for in/not_in).
    /// For RightKind=attribute it is the attribute key to read the comparand from.</summary>
    public object? RightValue { get; init; }
    public IReadOnlyList<AbacCondition> Children { get; init; } = Array.Empty<AbacCondition>();
}

/// <summary>One policy. Hydrated from authz.policy plus its condition tree.</summary>
public sealed class AbacPolicy
{
    public required string Key { get; init; }
    public required string Effect { get; init; }
    public required string ResourceType { get; init; }
    public required string Action { get; init; }
    public Guid? BrandId { get; init; }
    public string? PermissionCode { get; init; }
    public int Priority { get; init; } = 100;
    public DateTimeOffset EffectiveFrom { get; init; } = DateTimeOffset.MinValue;
    public DateTimeOffset? EffectiveTo { get; init; }
    public bool IsActive { get; init; } = true;
    /// <summary>Null = unconditional. This is how the 771 existing role_permissions rows project
    /// onto the model in A6.3 without inventing conditions they never had.</summary>
    public AbacCondition? Condition { get; init; }

    public bool IsInEffect(DateTimeOffset at) =>
        IsActive && EffectiveFrom <= at && (EffectiveTo is null || EffectiveTo > at);
}

/// <summary>What the PDP answers. Never throws — a failure is a Deny with a reason.</summary>
public enum AbacDecisionKind
{
    /// <summary>No policy applied. The caller must treat this as a denial (default-deny).</summary>
    NotApplicable,
    Permit,
    Deny,
}

public sealed record AbacDecision(
    AbacDecisionKind Kind,
    string? MatchedPolicyKey,
    string Reason)
{
    /// <summary>Default-deny: only an explicit Permit allows the request through.</summary>
    public bool IsAllowed => Kind == AbacDecisionKind.Permit;

    public static AbacDecision Permit(string policyKey, string reason) =>
        new(AbacDecisionKind.Permit, policyKey, reason);

    public static AbacDecision Deny(string? policyKey, string reason) =>
        new(AbacDecisionKind.Deny, policyKey, reason);

    public static AbacDecision NotApplicable(string reason) =>
        new(AbacDecisionKind.NotApplicable, null, reason);
}
