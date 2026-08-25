using core.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetRoleSurface;

/// <summary>One of §6.1's eight named roles, in plain language.</summary>
public sealed record RolePresetDto(
    string Key, string Name, string Does, string DoesNot,
    string? RoleCode, bool IsPlatform, bool Available, string? RequiresFeature);

/// <summary>One of §6.2's ten permission groups, with what this preset can do in it.</summary>
public sealed record PermissionGroupDto(string Key, string Name);

/// <summary>§6.2's matrix cell: <c>full</c> · <c>view</c> · <c>own</c> · <c>none</c>.</summary>
public sealed record RoleGroupCellDto(string PresetKey, string GroupKey, string Level);

/// <summary>The whole simplified surface: 8 roles × 10 groups, plus the matrix between them.</summary>
public sealed record RoleSurfaceDto(
    IReadOnlyList<RolePresetDto> Presets,
    IReadOnlyList<PermissionGroupDto> Groups,
    IReadOnlyList<RoleGroupCellDto> Matrix);

public sealed record GetRoleSurfaceQuery : IQuery<RoleSurfaceDto>;

/// <summary>
/// The §6 surface: "max 8 roles, 10 permission groups (not 300 permissions), plain do/don't language".
///
/// <para>A PRESENTATION over the engine, not a replacement for it (§6.3). Every cell is DERIVED from
/// the real <c>role_permissions</c> grants of the engine role each preset maps to — it is not a
/// second, hand-maintained matrix. That matters: a hand-written summary drifts from the grants it
/// claims to describe, and a role matrix that lies is worse than no matrix, because people act on
/// it.</para>
///
/// <para>Presets follow features (§5): one whose feature the brand has not bought comes back
/// <c>Available = false</c> rather than being dropped, so the console can show "Rider — included
/// with Fleet" instead of silently hiding a role the owner may be looking for.</para>
/// </summary>
/// <summary>A permission reduced to the two fields the group matrix needs.</summary>
internal readonly record struct PermissionShape(string Module, string Action);

public class GetRoleSurfaceQueryHandler : IQueryHandler<GetRoleSurfaceQuery, RoleSurfaceDto>
{
    private readonly ICoreDbContext _db;
    private readonly laundryghar.Utilities.Services.ICurrentUser _user;

    public GetRoleSurfaceQueryHandler(ICoreDbContext db, laundryghar.Utilities.Services.ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<RoleSurfaceDto> HandleAsync(GetRoleSurfaceQuery q, CancellationToken ct)
    {
        var presets = await _db.RolePresets.AsNoTracking()
            .OrderBy(p => p.SortOrder).ToListAsync(ct);
        var groups = await _db.PermissionGroups.AsNoTracking()
            .OrderBy(g => g.SortOrder).ToListAsync(ct);

        var entitled = await BrandFeatureGate.EntitledFeaturesAsync(_db, _user.TryGetBrandId(), ct);

        // The real grants, per engine role. This is what makes the summary honest.
        var roleCodes = presets.Where(p => p.RoleCode is not null).Select(p => p.RoleCode!).ToList();
        var grants = await _db.Roles.AsNoTracking()
            .Where(r => roleCodes.Contains(r.Code) && r.DeletedAt == null)
            .Select(r => new
            {
                r.Code,
                Permissions = r.RolePermissions
                    .Where(rp => rp.Effect != "deny")
                    .Select(rp => new PermissionShape(rp.Permission.Module, rp.Permission.Action))
                    .ToList(),
            })
            .ToListAsync(ct);

        var byRole = grants.ToDictionary(g => g.Code, g => g.Permissions, StringComparer.OrdinalIgnoreCase);

        var matrix = new List<RoleGroupCellDto>();
        foreach (var preset in presets)
        {
            foreach (var group in groups)
                matrix.Add(new RoleGroupCellDto(preset.Key, group.Key, LevelFor(preset, group, byRole)));
        }

        return new RoleSurfaceDto(
            presets.Select(p => new RolePresetDto(
                p.Key, p.Name, p.Does, p.DoesNot, p.RoleCode, p.IsPlatform,
                Available: BrandFeatureGate.IsRoleAvailable(p.RequiresFeature, entitled),
                p.RequiresFeature)).ToList(),
            groups.Select(g => new PermissionGroupDto(g.Key, g.Name)).ToList(),
            matrix);
    }

    /// <summary>
    /// Collapses a preset's real grants in one group down to a single word.
    ///
    /// <para>Deliberately pessimistic: a group reads <c>full</c> only if the role actually holds a
    /// mutating permission in it. Over-stating a role's power in a summary an owner delegates from is
    /// the failure that matters — under-stating it merely looks cautious.</para>
    /// </summary>
    private static string LevelFor(
        RolePreset preset, PermissionGroup group,
        Dictionary<string, List<PermissionShape>> byRole)
    {
        // `customer` maps to no engine role at all: their abilities are implicit and self-scoped
        // (docs/rbac.md §2), so the honest answer is "own records only", never "none".
        if (preset.RoleCode is null)
            return group.Key is "bookings" or "customers" or "money" or "processing" ? "own" : "none";

        if (!byRole.TryGetValue(preset.RoleCode, out var perms)) return "none";

        var inGroup = perms
            .Where(p => group.PermissionModules.Contains(p.Module, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (inGroup.Count == 0) return "none";

        var mutating = new[] { "create", "update", "delete", "manage", "approve", "publish",
                               "assign", "cancel", "refund", "override" };

        return inGroup.Any(p => mutating.Contains(p.Action, StringComparer.OrdinalIgnoreCase))
            ? "full"
            : "view";
    }


}
