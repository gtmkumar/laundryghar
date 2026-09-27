using core.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Users.Common;

/// <summary>
/// Restricts a user query to the caller's own tenant.
///
/// <para><b>Why this has to exist at all.</b> <c>identity_access.users</c> carries no
/// <c>brand_id</c> column, so there is nothing for row-level security to key on and no RLS policy
/// protects it. A user's tenant is a derived fact — it comes from their live memberships, through
/// the scope node those memberships point at. That makes tenant isolation for users a purely
/// application-level responsibility, and therefore something every query has to remember to do.</para>
///
/// <para><b>What went wrong without it.</b> A live audit found <c>GetUsers</c> returning every user
/// on the platform to any brand admin, and <c>GetUserById</c> returning any user's full profile —
/// email, phone, PAN, masked Aadhaar, bank account, IFSC, UPI — to a caller from an unrelated
/// tenant. Neither handler injected <c>ICurrentUser</c> at all. Meanwhile <c>GetAccessPeople</c>
/// had the correct rule all along, written inline and in memory. One rule, three queries, and the
/// two that mattered most did not have it. That is the shape this helper exists to prevent: there
/// is now one implementation, and a new user query gets tenant isolation by calling it.</para>
///
/// <para>Composed in the database rather than filtered in memory, so it survives pagination — an
/// in-memory filter over a page would silently return short pages and, worse, would only hide the
/// rows it happened to have fetched.</para>
/// </summary>
public static class UserBrandScope
{
    /// <summary>
    /// Keeps only users whose live memberships resolve to the caller's brand.
    ///
    /// <para>A platform admin with no brand selected is left unfiltered — cross-tenant visibility is
    /// their job, and this mirrors <c>GetAccessPeople</c>'s documented degradation. The moment they
    /// pick a brand via <c>X-Brand-Id</c>, <see cref="ICurrentUser.TryGetBrandId"/> returns it and
    /// they are scoped like everyone else.</para>
    /// </summary>
    public static IQueryable<User> ScopedToCallerBrand(
        this IQueryable<User> users, ICoreDbContext db, ICurrentUser actor)
    {
        if (actor.TryGetBrandId() is not Guid brandId) return users;

        return users.Where(u => db.UserScopeMemberships.Any(m =>
            m.UserId == u.Id
            && m.RevokedAt == null
            && (m.ExpiresAt == null || m.ExpiresAt > DateTimeOffset.UtcNow)
            && (
                // The membership names the brand directly…
                (m.ScopeType == ScopeType.Brand && m.ScopeId == brandId)
                // …or names a node beneath it. Resolved through the owning table rather than
                // trusted from the membership row, so moving a store between brands moves its
                // staff with it.
                || (m.ScopeType == ScopeType.Franchise
                    && db.Franchises.Any(f => f.Id == m.ScopeId && f.BrandId == brandId))
                || (m.ScopeType == ScopeType.Store
                    && db.Stores.Any(s => s.Id == m.ScopeId && s.BrandId == brandId))
                || (m.ScopeType == ScopeType.Warehouse
                    && db.Warehouses.Any(w => w.Id == m.ScopeId && w.BrandId == brandId))
            )));

        // Note what is deliberately EXCLUDED: a platform-scoped membership. Platform staff are not
        // any brand's people and must not appear in a tenant's directory — the same call
        // GetAccessPeople makes.
    }
}
