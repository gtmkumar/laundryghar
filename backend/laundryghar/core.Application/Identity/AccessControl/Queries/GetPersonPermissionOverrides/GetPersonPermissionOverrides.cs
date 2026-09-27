using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.Users.Common;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetPersonPermissionOverrides;

public sealed record GetPersonPermissionOverridesQuery(Guid UserId)
    : IQuery<IReadOnlyList<PersonPermissionOverrideDto>>;

/// <summary>
/// Every live per-user permission override one person holds.
///
/// <para><b>Why it did not exist.</b> Audit finding A-4, "blind writes", second half — and the ABAC
/// plan had already named it independently (task A8.2: "<c>user_permission_override</c> is
/// write-only with no query to read it back"). The panel could set and clear an override but never
/// show one, so the only way to answer "what exceptions does this person carry" was to open the
/// database. An override is the sharpest instrument in the model — it layers a single allow or deny
/// on top of a role, and deny always wins — which makes it the worst thing to leave unreadable.</para>
///
/// <para>Tenancy is the same rule as <see cref="Queries.GetPersonMemberships.GetPersonMembershipsQueryHandler"/>:
/// the target is resolved through <see cref="UserBrandScope.ScopedToCallerBrand"/>, and a person the
/// caller cannot see returns an empty list rather than a distinguishable error.</para>
///
/// <para>Expired rows are omitted because permission resolution already ignores them — showing one
/// would report authority the user does not have. Scoped overrides carry the resolved node name for
/// the same reason the membership query does.</para>
/// </summary>
public class GetPersonPermissionOverridesQueryHandler
    : IQueryHandler<GetPersonPermissionOverridesQuery, IReadOnlyList<PersonPermissionOverrideDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;

    public GetPersonPermissionOverridesQueryHandler(ICoreDbContext db, ICurrentUser actor)
    {
        _db = db;
        _actor = actor;
    }

    public async Task<IReadOnlyList<PersonPermissionOverrideDto>> HandleAsync(
        GetPersonPermissionOverridesQuery q, CancellationToken ct)
    {
        var visible = await _db.Users.AsNoTracking()
            .ScopedToCallerBrand(_db, _actor)
            .AnyAsync(u => u.Id == q.UserId && u.DeletedAt == null, ct);

        if (!visible) return [];

        var now = DateTimeOffset.UtcNow;

        return await _db.UserPermissionOverrides.AsNoTracking()
            .Where(o => o.UserId == q.UserId && (o.ExpiresAt == null || o.ExpiresAt > now))
            .Join(_db.Permissions, o => o.PermissionId, p => p.Id, (o, p) => new { o, p })
            // Deny first — it is the stronger statement and the one an admin most needs to notice —
            // then by code so the list is stable between reads. Ordered before the projection: EF
            // cannot translate an ORDER BY over a constructed record.
            .OrderByDescending(x => x.o.Effect == "deny")
            .ThenBy(x => x.p.Code)
            .Select(x => new PersonPermissionOverrideDto(
                x.o.Id,
                x.o.UserId,
                x.p.Code,
                x.p.Name,
                x.p.Module,
                x.o.Effect,
                x.o.ScopeType,
                x.o.ScopeId,
                x.o.ScopeType == null
                    ? null
                    : x.o.ScopeType == ScopeType.Platform
                        ? "Platform"
                        : x.o.ScopeType == ScopeType.Brand
                            ? _db.Brands.Where(b => b.Id == x.o.ScopeId).Select(b => b.Name).FirstOrDefault()
                        : x.o.ScopeType == ScopeType.Franchise
                            ? _db.Franchises.Where(f => f.Id == x.o.ScopeId).Select(f => f.LegalName).FirstOrDefault()
                        : x.o.ScopeType == ScopeType.Store
                            ? _db.Stores.Where(s => s.Id == x.o.ScopeId).Select(s => s.Name).FirstOrDefault()
                        : x.o.ScopeType == ScopeType.Warehouse
                            ? _db.Warehouses.Where(w => w.Id == x.o.ScopeId).Select(w => w.Name).FirstOrDefault()
                        : null,
                x.o.Reason,
                x.o.ExpiresAt,
                x.o.GrantedAt))
            .ToListAsync(ct);
    }
}
