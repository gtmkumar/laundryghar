namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// One of the eight named roles PLATFORM_STRATEGY.md §6.1 puts in front of an owner — a PRESET over
/// the scoped-RBAC engine, never a replacement for it (§6.3: "the engine's power stays").
/// <see cref="RoleCode"/> is the engine role actually granted.
/// </summary>
public class RolePreset
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    /// <summary>§6's plain-language "DOES" column — what an owner reads instead of a matrix.</summary>
    public string Does { get; set; } = null!;
    /// <summary>§6's "DOES NOT" column. The boundary is the useful half: it is what stops someone
    /// granting Manager when they meant Staff.</summary>
    public string DoesNot { get; set; } = null!;
    /// <summary>The engine role this grants. NULL for <c>customer</c>, which is outside the staff
    /// RBAC graph entirely (docs/rbac.md §2) and appears only to complete the §6 picture.</summary>
    public string? RoleCode { get; set; }
    /// <summary>Feature the brand must own for this preset to be offered (§5 "roles follow features").</summary>
    public string? RequiresFeature { get; set; }
    /// <summary>Ours, not the provider's — §8's line between platform and company.</summary>
    public bool IsPlatform { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// One of §6.2's ten permission groups: a plain-language row that stands in for many
/// <c>module.action</c> permissions. <see cref="PermissionModules"/> reuses the same reverse-mapping
/// shape as <c>modules.permission_modules</c>, so the summary and the detailed matrix are driven by
/// one catalogue and cannot drift apart.
/// </summary>
public class PermissionGroup
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string[] PermissionModules { get; set; } = [];
    public int SortOrder { get; set; }
}
