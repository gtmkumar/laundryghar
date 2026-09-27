using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>The answer to one gate check: what the PDP decided, and whether that decision blocks.</summary>
/// <param name="Decision">The PDP's verdict, always populated — even in shadow mode.</param>
/// <param name="Enforced">
/// True when this resource type is cut over to enforcement. In shadow mode this is false and
/// <see cref="IsBlocked"/> is false regardless of the verdict, which is the whole point of shadow.
/// </param>
public sealed record AbacGateResult(AbacDecision Decision, bool Enforced)
{
    public bool IsBlocked => Enforced && !Decision.IsAllowed;

    /// <summary>Allowed-and-not-evaluated, for the disabled path.</summary>
    public static readonly AbacGateResult Skipped =
        new(AbacDecision.Permit("engine-disabled", "ABAC is disabled."), Enforced: false);
}

/// <summary>
/// The one entry point everything else uses: build attributes, fetch policies, decide, log.
/// </summary>
public interface IAbacAuthorizationService
{
    /// <param name="rbacAllowed">
    /// What the existing permission gate decided for this same request, when the caller can see it.
    /// Recorded alongside the ABAC verdict so a shadow window produces a parity diff (A6.2) without
    /// any replay infrastructure — both answers come from one evaluation of one real request.
    /// </param>
    Task<AbacGateResult> EvaluateAsync(
        string resourceType, string action, AbacResourceRef? resource, CancellationToken ct,
        bool? rbacAllowed = null);
}

/// <inheritdoc cref="IAbacAuthorizationService"/>
public sealed class AbacAuthorizationService : IAbacAuthorizationService
{
    private readonly IAbacAttributeProvider _attributes;
    private readonly IPolicyRepository _policies;
    private readonly IPolicyDecisionPoint _pdp;
    private readonly IDecisionLogWriter? _log;
    private readonly AbacOptions _options;
    private readonly TimeProvider _clock;

    public AbacAuthorizationService(
        IAbacAttributeProvider attributes,
        IPolicyRepository policies,
        IPolicyDecisionPoint pdp,
        IOptions<AbacOptions> options,
        IDecisionLogWriter? log = null,
        TimeProvider? clock = null)
    {
        _attributes = attributes;
        _policies = policies;
        _pdp = pdp;
        _options = options.Value;
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<AbacGateResult> EvaluateAsync(
        string resourceType, string action, AbacResourceRef? resource, CancellationToken ct,
        bool? rbacAllowed = null)
    {
        if (!_options.Enabled) return AbacGateResult.Skipped;

        var started = Stopwatch.GetTimestamp();
        var now = _clock.GetUtcNow();

        var bag = await _attributes.BuildAsync(resourceType, action, resource, ct);
        var policies = await _policies.GetAsync(resourceType, action, ct);

        var request = new AbacRequest(
            resourceType, action, bag, now,
            SubjectBrandId: bag.TryGet(AbacAttributeKeys.SubjectBrandId, out var b) ? b as Guid? : null);

        var decision = _pdp.Decide(request, policies);
        var enforced = _options.EnforcesFor(resourceType);

        Log(resourceType, action, resource, bag, decision, enforced, now,
            (int)(Stopwatch.GetElapsedTime(started).TotalMicroseconds), rbacAllowed);

        return new AbacGateResult(decision, enforced);
    }

    private void Log(
        string resourceType, string action, AbacResourceRef? resource, AbacAttributeBag bag,
        AbacDecision decision, bool enforced, DateTimeOffset now, int latencyMicroseconds,
        bool? rbacAllowed)
    {
        if (_log is null || !_options.LogDecisions) return;

        // A permit that AGREES with RBAC is the boring majority and can be sampled out. A permit
        // that DISAGREES is the dangerous kind — ABAC widening access — so it is always logged,
        // whatever LogPermits says. Dropping those would hide the one signal that must never be
        // missed during a shadow window.
        var disagrees = rbacAllowed is { } r && r != decision.IsAllowed;
        if (decision.IsAllowed && !_options.LogPermits && !disagrees) return;

        _log.Enqueue(new AbacDecisionRecord(
            OccurredAt: now,
            BrandId: Get<Guid>(bag, AbacAttributeKeys.SubjectBrandId),
            UserId: Get<Guid>(bag, AbacAttributeKeys.SubjectUserId),
            CustomerId: Get<Guid>(bag, AbacAttributeKeys.SubjectCustomerId),
            TokenUse: bag[AbacAttributeKeys.SubjectTokenUse] as string,
            ResourceType: resourceType,
            ResourceId: resource?.ResourceId ?? Get<Guid>(bag, AbacAttributeKeys.ResourceId),
            Action: action,
            Decision: decision.Kind switch
            {
                AbacDecisionKind.Permit => "permit",
                AbacDecisionKind.Deny => "deny",
                _ => "not_applicable",
            },
            Mode: enforced ? AbacMode.Enforce : AbacMode.Shadow,
            MatchedPolicy: decision.MatchedPolicyKey,
            Reason: decision.Reason,
            AttributesJson: AbacAttributeSerializer.Serialize(bag),
            LatencyMicroseconds: latencyMicroseconds,
            RbacAllowed: rbacAllowed));
    }

    private static T? Get<T>(AbacAttributeBag bag, string key) where T : struct
        => bag.TryGet(key, out var v) && v is T typed ? typed : null;
}
