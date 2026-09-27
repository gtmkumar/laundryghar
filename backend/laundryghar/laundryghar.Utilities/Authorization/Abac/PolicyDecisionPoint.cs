namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>The PDP contract. Never throws: a failure is a Deny carrying a reason (A3.4).</summary>
public interface IPolicyDecisionPoint
{
    AbacDecision Decide(AbacRequest request, IReadOnlyList<AbacPolicy> policies);
}

/// <summary>One authorization question.</summary>
public sealed record AbacRequest(
    string ResourceType,
    string Action,
    AbacAttributeBag Attributes,
    DateTimeOffset At,
    Guid? SubjectBrandId = null);

/// <summary>
/// Combining algorithm: deny-overrides, then priority, then default-deny (A3.2).
///
/// This reproduces the semantics the RBAC engine already has — ScopeResolver.cs:119-124 subtracts
/// the union of every membership's denies from the union of its allows, so a deny anywhere kills a
/// permission everywhere — and extends it with the fail-closed handling RBAC never needed, because
/// a set-membership test cannot be Indeterminate but a condition over attributes can.
/// </summary>
public sealed class PolicyDecisionPoint : IPolicyDecisionPoint
{
    public AbacDecision Decide(AbacRequest request, IReadOnlyList<AbacPolicy> policies)
    {
        var applicable = policies
            .Where(p => IsApplicable(p, request))
            .OrderBy(p => p.Priority)
            .ToList();

        if (applicable.Count == 0)
            return AbacDecision.NotApplicable(
                $"No policy covers {request.ResourceType}/{request.Action}.");

        // ── Pass 1: denies win, and an unevaluable deny wins too ──────────────────────────────
        // Treating Indeterminate as "not matched" here would be the fail-open bug this engine
        // exists to avoid: a deny whose attribute the PIP could not resolve would silently vanish.
        foreach (var policy in applicable.Where(p => p.Effect == PolicyEffect.Deny))
        {
            switch (ConditionEvaluator.Evaluate(policy.Condition, request.Attributes))
            {
                case Ternary.True:
                    return AbacDecision.Deny(policy.Key, $"Denied by policy '{policy.Key}'.");
                case Ternary.Indeterminate:
                    return AbacDecision.Deny(policy.Key,
                        $"Denied by policy '{policy.Key}': its condition could not be evaluated " +
                        "(an attribute was unresolved), and an unevaluable deny fails closed.");
            }
        }

        // ── Pass 2: the highest-priority permit whose condition actually holds ─────────────────
        AbacPolicy? indeterminatePermit = null;
        foreach (var policy in applicable.Where(p => p.Effect == PolicyEffect.Permit))
        {
            switch (ConditionEvaluator.Evaluate(policy.Condition, request.Attributes))
            {
                case Ternary.True:
                    return AbacDecision.Permit(policy.Key, $"Permitted by policy '{policy.Key}'.");
                case Ternary.Indeterminate:
                    indeterminatePermit ??= policy;
                    break;
            }
        }

        if (indeterminatePermit is not null)
            return AbacDecision.Deny(indeterminatePermit.Key,
                $"No permit applied: policy '{indeterminatePermit.Key}' could not be evaluated " +
                "(an attribute was unresolved).");

        return AbacDecision.Deny(null,
            $"No permit policy matched for {request.ResourceType}/{request.Action}.");
    }

    /// <summary>
    /// A policy applies when it targets this resource and action, is inside its effective window,
    /// and is either platform-authored (brand_id NULL) or owned by the subject's brand.
    ///
    /// The brand rule mirrors the RLS policy added in migration 0024 — and deliberately keeps the
    /// `brand_id IS NULL` arm that identity_access.roles omits, which is why a brand admin there
    /// cannot see a single system role.
    /// </summary>
    private static bool IsApplicable(AbacPolicy p, AbacRequest r)
        => p.IsInEffect(r.At)
        && string.Equals(p.ResourceType, r.ResourceType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(p.Action, r.Action, StringComparison.OrdinalIgnoreCase)
        && (p.BrandId is null || p.BrandId == r.SubjectBrandId);
}
