namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>
/// Runtime configuration for the ABAC engine. Bound from the <c>Abac</c> configuration section.
///
/// <para>The defaults are the safe ones: the engine is OFF, and turning it on puts it in shadow
/// mode. Nothing about who can do what changes until someone deliberately sets
/// <c>Abac:Mode = enforce</c> for a named module — and A6.2's parity harness is what earns that.</para>
/// </summary>
public sealed class AbacOptions
{
    public const string SectionName = "Abac";

    /// <summary>Master switch. False = the PDP is never consulted and no decision rows are written.</summary>
    public bool Enabled { get; set; }

    /// <summary>shadow | enforce. See <see cref="AbacMode"/>.</summary>
    public string Mode { get; set; } = AbacMode.Shadow;

    /// <summary>
    /// Resource types cut over to enforcement, evaluated only when <see cref="Mode"/> is
    /// <c>enforce</c>. Empty means every resource type. This is the A7.1 per-module dial: commerce
    /// first, then operations, then core, each with its own parity window, so a bad policy costs one
    /// module rather than the platform.
    /// </summary>
    public IList<string> EnforcedResourceTypes { get; set; } = [];

    /// <summary>Write a decision_log row for permits as well as denies. Denies are always logged
    /// when logging is on; permits are the high-volume half and are opt-in.</summary>
    public bool LogPermits { get; set; } = true;

    /// <summary>Off switch for decision logging alone, independent of enforcement.</summary>
    public bool LogDecisions { get; set; } = true;

    public bool IsShadow => !string.Equals(Mode, AbacMode.Enforce, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a denial for this resource type should actually block the request.</summary>
    public bool EnforcesFor(string resourceType)
    {
        if (!Enabled || IsShadow) return false;
        if (EnforcedResourceTypes.Count == 0) return true;
        return EnforcedResourceTypes.Contains(resourceType, StringComparer.OrdinalIgnoreCase);
    }
}

public static class AbacMode
{
    /// <summary>Evaluate and log, never block. The only safe way to discover what a policy set
    /// would have done to real traffic.</summary>
    public const string Shadow = "shadow";

    /// <summary>Evaluate and block on deny.</summary>
    public const string Enforce = "enforce";
}
