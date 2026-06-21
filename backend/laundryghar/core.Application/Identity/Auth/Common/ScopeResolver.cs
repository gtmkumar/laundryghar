using core.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Auth.Common;

/// <summary>
/// Resolves a user's active scope memberships → roles → permissions,
/// and builds TokenClaims ready for JWT issuance.
/// </summary>
public static class ScopeResolver
{
    /// <summary>
    /// Loads the user's active memberships + role permissions from the DB,
    /// then picks an active scope (primary, or override if valid) and builds claims.
    /// </summary>
    public static async Task<TokenClaims> BuildTokenClaimsAsync(
        ICoreDbContext db,
        User user,
        string? requestedScopeType = null,
        Guid? requestedScopeId = null,
        bool enforceEntitlement = false,
        CancellationToken ct = default)
    {
        // Load active memberships with roles and their permissions
        var memberships = await db.UserScopeMemberships
            .AsNoTracking()
            .Where(m => m.UserId == user.Id
                     && m.RevokedAt == null
                     && (m.ExpiresAt == null || m.ExpiresAt > DateTimeOffset.UtcNow))
            .Include(m => m.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .ToListAsync(ct);

        // Select active scope: requested → primary → first
        UserScopeMembership? activeMembership = null;

        if (requestedScopeType is not null)
        {
            activeMembership = memberships.FirstOrDefault(m =>
                m.ScopeType == requestedScopeType
                && m.ScopeId == requestedScopeId);
        }

        activeMembership ??= memberships.FirstOrDefault(m => m.IsPrimary)
                          ?? memberships.FirstOrDefault();

        // Resolve effective permissions across ALL the user's active memberships at the SAME
        // scope as the active one (a user may hold several roles at one brand — e.g. Operations
        // Manager + Finance Manager; previously only one counted). Then layer per-user
        // overrides. Allow/deny semantics, DENY WINS:
        //   effective = (role-allowed − role-denied ∪ user-allow) − user-deny
        var roleAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleDenied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (activeMembership is not null)
        {
            foreach (var m in memberships.Where(m =>
                         m.ScopeType == activeMembership.ScopeType && m.ScopeId == activeMembership.ScopeId))
                foreach (var rp in m.Role.RolePermissions)
                {
                    if (rp.Effect == "deny") roleDenied.Add(rp.Permission.Code);
                    else roleAllowed.Add(rp.Permission.Code);
                }
        }

        // Per-user overrides (apply across scopes; deny always wins).
        var overrides = await db.UserPermissionOverrides.AsNoTracking()
            .Where(o => o.UserId == user.Id)
            .Select(o => new { o.Effect, o.Permission.Code })
            .ToListAsync(ct);
        var userAllow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userDeny = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in overrides)
            (o.Effect == "deny" ? userDeny : userAllow).Add(o.Code);

        // effective = (roleAllowed − roleDenied ∪ userAllow) − userDeny
        var effective = new HashSet<string>(roleAllowed, StringComparer.OrdinalIgnoreCase);
        effective.ExceptWith(roleDenied);
        effective.UnionWith(userAllow);
        effective.ExceptWith(userDeny);

        var permissions = new HashSet<string>(effective, StringComparer.OrdinalIgnoreCase);

        // Determine tenant context from scope
        Guid? brandId     = null;
        Guid? franchiseId = null;
        Guid? storeId     = null;

        if (activeMembership is not null)
        {
            switch (activeMembership.ScopeType)
            {
                case ScopeType.Brand:
                    brandId = activeMembership.ScopeId;
                    break;
                case ScopeType.Franchise:
                    franchiseId = activeMembership.ScopeId;
                    // Resolve brand from franchise
                    var franchise = await db.Franchises
                        .AsNoTracking()
                        .Where(f => f.Id == franchiseId)
                        .Select(f => new { f.BrandId })
                        .FirstOrDefaultAsync(ct);
                    if (franchise is not null) brandId = franchise.BrandId;
                    break;
                case ScopeType.Store:
                    storeId = activeMembership.ScopeId;
                    var store = await db.Stores
                        .AsNoTracking()
                        .Where(s => s.Id == storeId)
                        .Select(s => new { s.BrandId, s.FranchiseId })
                        .FirstOrDefaultAsync(ct);
                    if (store is not null)
                    {
                        brandId     = store.BrandId;
                        franchiseId = store.FranchiseId;
                    }
                    break;
                case ScopeType.Warehouse:
                    var warehouse = await db.Warehouses
                        .AsNoTracking()
                        .Where(w => w.Id == activeMembership.ScopeId)
                        .Select(w => new { w.BrandId, w.FranchiseId })
                        .FirstOrDefaultAsync(ct);
                    if (warehouse is not null)
                    {
                        brandId     = warehouse.BrandId;
                        franchiseId = warehouse.FranchiseId;
                    }
                    break;
                // platform scope: no tenant narrowing → bypass RLS
            }
        }

        // PaaS entitlement (Phase 3, behind a flag): keep only permissions whose CANONICAL
        // owning module (permissions.module_key) the brand has licensed (or that is core).
        // module_key is a single owner per permission (set by permission_canonical_module.sql),
        // so there is no tag-overlap ambiguity; orphans (null module_key) are always kept.
        // Baking the filtered set into the token means every API endpoint enforces entitlement
        // automatically via HasPermission — no hot-path change. Platform admins are exempt
        // (cross-brand operators). Runs on the bypass_rls auth path, so the brand_module read
        // sees rows; the explicit brand filter keeps it correct.
        if (enforceEntitlement && brandId is { } entBrandId
            && user.UserType != UserType.PlatformAdmin)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var entitledKeys = (await db.Modules.AsNoTracking()
                .Where(m => m.Status == "active" && (m.IsCore ||
                    db.BrandModules.Any(bm => bm.BrandId == entBrandId && bm.ModuleKey == m.Key
                        && bm.Enabled && (bm.ValidUntil == null || bm.ValidUntil >= today))))
                .Select(m => m.Key)
                .ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Map each effective code → its canonical owning module.
            var ownerByCode = await db.Permissions.AsNoTracking()
                .Where(p => permissions.Contains(p.Code))
                .Select(p => new { p.Code, p.ModuleKey })
                .ToListAsync(ct);
            var ownerLookup = ownerByCode.ToDictionary(x => x.Code, x => x.ModuleKey, StringComparer.OrdinalIgnoreCase);

            permissions = permissions.Where(c =>
            {
                var owner = ownerLookup.GetValueOrDefault(c);
                return owner is null || entitledKeys.Contains(owner); // orphan → keep
            }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new TokenClaims(
            UserId:      user.Id,
            UserType:    user.UserType,
            Email:       user.Email,
            Phone:       user.PhoneE164,
            ScopeType:   activeMembership?.ScopeType,
            ScopeId:     activeMembership?.ScopeId,
            BrandId:     brandId,
            FranchiseId: franchiseId,
            StoreId:     storeId,
            Permissions: string.Join(' ', permissions),
            PermVersion: user.PermVersion);
    }
}
