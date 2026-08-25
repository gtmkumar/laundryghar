using core.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl;

/// <summary>
/// "Roles follow features" (PLATFORM_STRATEGY.md §5/§6.3): the features a brand may operate, so a
/// role gated on a feature the brand has not bought is neither offered nor grantable.
///
/// <para>Shared by the read path (<c>GetAccessRoles</c> — don't show it) and the write path
/// (<c>GrantMembership</c> — don't allow it). Both matter: hiding alone is cosmetic, and anyone who
/// can craft the request could still grant a Rider role on a brand with no fleet.</para>
/// </summary>
public static class BrandFeatureGate
{
    /// <summary>
    /// The feature keys this brand may operate: everything core, plus what it has licensed on an
    /// enabled, unexpired row.
    ///
    /// <para>Returns <c>null</c> when there is no brand in context (a platform admin with no brand
    /// selected). Null means "do not gate" — a platform operator sees the whole catalogue. Callers
    /// must treat null and empty very differently: empty means the brand owns nothing.</para>
    /// </summary>
    public static async Task<HashSet<string>?> EntitledFeaturesAsync(
        ICoreDbContext db, Guid? brandId, CancellationToken ct)
    {
        if (brandId is not { } id) return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var licensed = (await db.BrandFeatures.AsNoTracking()
            .Where(bf => bf.BrandId == id && bf.Enabled
                      && (bf.ValidUntil == null || bf.ValidUntil >= today))
            .Select(bf => bf.FeatureKey)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var core = await db.Features.AsNoTracking()
            .Where(f => f.Status == "active" && f.IsCore)
            .Select(f => f.Key)
            .ToListAsync(ct);

        licensed.UnionWith(core);
        return licensed;
    }

    /// <summary>
    /// Is a role whose gate is <paramref name="roleFeatureKey"/> available to this brand?
    /// An ungated role (null) always is; with no brand context (<paramref name="entitled"/> null)
    /// everything is.
    /// </summary>
    public static bool IsRoleAvailable(string? roleFeatureKey, HashSet<string>? entitled)
        => roleFeatureKey is null || entitled is null || entitled.Contains(roleFeatureKey);
}
