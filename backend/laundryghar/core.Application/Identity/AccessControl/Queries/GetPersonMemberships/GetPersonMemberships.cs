using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.Users.Common;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetPersonMemberships;

public sealed record GetPersonMembershipsQuery(Guid UserId) : IQuery<IReadOnlyList<PersonMembershipDto>>;

/// <summary>
/// Every live scope membership one person holds.
///
/// <para><b>Why it did not exist.</b> Audit finding A-4, "blind writes": the person drawer could
/// GRANT and REVOKE memberships but nothing could LIST them, so the panel could only offer to
/// revoke what the current browser session had just granted — it said so in its own copy, and
/// anything granted yesterday was unreachable. Write-only authority is the worst kind to leave
/// unreadable: nobody can answer "what does this person actually hold" without opening the
/// database.</para>
///
/// <para><b>Tenant isolation.</b> The target is resolved through
/// <see cref="UserBrandScope.ScopedToCallerBrand"/> rather than by id alone — the same predicate
/// <c>GetUsers</c> and <c>GetUserById</c> use since F-1/F-2, and the reason those two leaked was
/// that each had written the rule out separately and two of three had forgotten it. A person
/// outside the caller's brand is reported as not found, not as an empty list, so the endpoint
/// cannot be used to probe which user ids exist.</para>
///
/// <para>Memberships are then filtered to the caller's own brand as well: a person may legitimately
/// hold memberships under two brands, and one brand's admin has no business seeing the other's.
/// A platform admin with no brand selected sees everything, mirroring the helper's own rule.</para>
/// </summary>
public class GetPersonMembershipsQueryHandler
    : IQueryHandler<GetPersonMembershipsQuery, IReadOnlyList<PersonMembershipDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;

    public GetPersonMembershipsQueryHandler(ICoreDbContext db, ICurrentUser actor)
    {
        _db = db;
        _actor = actor;
    }

    public async Task<IReadOnlyList<PersonMembershipDto>> HandleAsync(
        GetPersonMembershipsQuery q, CancellationToken ct)
    {
        var visible = await _db.Users.AsNoTracking()
            .ScopedToCallerBrand(_db, _actor)
            .AnyAsync(u => u.Id == q.UserId && u.DeletedAt == null, ct);

        if (!visible) return [];

        var brandId = _actor.TryGetBrandId();
        // Hoisted so EF captures a constant rather than trying to translate the property.
        var includePlatform = _actor.IsPlatformAdmin;
        var now = DateTimeOffset.UtcNow;

        var rows = await _db.UserScopeMemberships.AsNoTracking()
            .Where(m => m.UserId == q.UserId
                     && m.RevokedAt == null
                     && (m.ExpiresAt == null || m.ExpiresAt > now))
            // Same brand test as UserBrandScope, applied to the membership rather than the person:
            // resolved through the owning table, never trusted from the membership row.
            .Where(m => brandId == null
                     // A platform-scoped membership belongs to no brand, so the tests below can
                     // never match it. It is shown to a platform admin and to nobody else — which
                     // is both halves of the rule: GrantMembership only lets a platform admin
                     // create one (a platform target resolves to an all-null ancestor chain, which
                     // only IsWithinScope's platform arm satisfies), so without this the one caller
                     // who can make such a membership is the one caller who cannot see it — the
                     // blind write A-4 is about, reintroduced. Brand admins still do not see
                     // platform staff structure, matching UserBrandScope's own exclusion.
                     || (includePlatform && m.ScopeType == ScopeType.Platform)
                     || (m.ScopeType == ScopeType.Brand && m.ScopeId == brandId)
                     || (m.ScopeType == ScopeType.Franchise
                         && _db.Franchises.Any(f => f.Id == m.ScopeId && f.BrandId == brandId))
                     || (m.ScopeType == ScopeType.Store
                         && _db.Stores.Any(s => s.Id == m.ScopeId && s.BrandId == brandId))
                     || (m.ScopeType == ScopeType.Warehouse
                         && _db.Warehouses.Any(w => w.Id == m.ScopeId && w.BrandId == brandId)))
            .Join(_db.Roles.IgnoreQueryFilters(), m => m.RoleId, r => r.Id, (m, r) => new { m, r })
            // Primary first, then most recently granted — the order the panel reads them in.
            // Ordered BEFORE the projection: EF cannot translate an ORDER BY over a constructed
            // DTO, and doing it after compiles fine but throws at runtime.
            .OrderByDescending(x => x.m.IsPrimary)
            .ThenByDescending(x => x.m.GrantedAt)
            .Select(x => new PersonMembershipDto(
                x.m.Id,
                x.m.UserId,
                x.m.ScopeType,
                x.m.ScopeId,
                // Resolved here rather than in the client, so the panel does not need every
                // franchise/store/warehouse list loaded just to name one scope.
                x.m.ScopeType == ScopeType.Platform
                    ? "Platform"
                    : x.m.ScopeType == ScopeType.Brand
                        ? _db.Brands.Where(b => b.Id == x.m.ScopeId).Select(b => b.Name).FirstOrDefault()
                    : x.m.ScopeType == ScopeType.Franchise
                        ? _db.Franchises.Where(f => f.Id == x.m.ScopeId).Select(f => f.LegalName).FirstOrDefault()
                    : x.m.ScopeType == ScopeType.Store
                        ? _db.Stores.Where(s => s.Id == x.m.ScopeId).Select(s => s.Name).FirstOrDefault()
                    : x.m.ScopeType == ScopeType.Warehouse
                        ? _db.Warehouses.Where(w => w.Id == x.m.ScopeId).Select(w => w.Name).FirstOrDefault()
                    : null,
                x.r.Id,
                x.r.Code,
                x.r.Name,
                x.m.IsPrimary,
                x.m.GrantedAt,
                x.m.ExpiresAt))
            .ToListAsync(ct);

        return rows;
    }
}
