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

        // Collect permissions from the active membership's role
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (activeMembership is not null)
        {
            foreach (var rp in activeMembership.Role.RolePermissions)
                permissions.Add(rp.Permission.Code);
        }

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
            Permissions: string.Join(' ', permissions));
    }
}
