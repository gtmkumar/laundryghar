using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetAccessPeople;

public sealed record GetAccessPeopleQuery(string? Search, int Page, int PageSize, Guid? FranchiseId = null, string? Sort = null)
    : IQuery<AccessPeoplePageDto>;

public class GetAccessPeopleQueryHandler : IQueryHandler<GetAccessPeopleQuery, AccessPeoplePageDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public GetAccessPeopleQueryHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<AccessPeoplePageDto> HandleAsync(GetAccessPeopleQuery q, CancellationToken ct)
    {
        // Org users (exclude pure riders — they live on the Riders screen).
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status != "deleted" && u.UserType != "rider")
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.Status,
                u.UserType,
                u.LastActiveAt,
                u.LastLoginAt,
                First = u.Profile != null ? u.Profile.FirstName : null,
                Last = u.Profile != null ? u.Profile.LastName : null,
                Display = u.Profile != null ? u.Profile.DisplayName : null,
                Membership = u.ScopeMemberships
                    .Where(m => m.RevokedAt == null)
                    .OrderByDescending(m => m.IsPrimary)
                    .Select(m => new { m.ScopeType, m.ScopeId, RoleCode = m.Role.Code, RoleName = m.Role.Name, RoleScope = m.Role.ScopeType })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Franchise-scoped staff view: keep only members of this franchise (its
        // own scope or a store within it), excluding the owner (shown separately).
        if (q.FranchiseId is Guid fid)
        {
            var storeIds = (await _db.Stores.AsNoTracking()
                .Where(s => s.FranchiseId == fid && s.DeletedAt == null)
                .Select(s => s.Id).ToListAsync(ct)).ToHashSet();
            users = users.Where(u => u.Membership != null
                && u.Membership.RoleCode != "franchise_owner"
                && ((u.Membership.ScopeType == "franchise" && u.Membership.ScopeId == fid)
                    || (u.Membership.ScopeType == "store" && u.Membership.ScopeId != null && storeIds.Contains(u.Membership.ScopeId.Value))))
                .ToList();
        }

        // Brand isolation: a brand-scoped admin (or a platform admin who has selected a brand via
        // X-Brand-Id) must only see people whose primary membership resolves to that same brand —
        // otherwise the directory leaks other tenants' staff. Degrades gracefully (shows everything)
        // for a platform admin with no brand context, preserving the prior cross-brand behaviour.
        if (_user.TryGetBrandId() is Guid brandId)
        {
            var brandFranchiseIds = (await _db.Franchises.AsNoTracking()
                .Where(f => f.BrandId == brandId).Select(f => f.Id).ToListAsync(ct)).ToHashSet();
            var brandStoreIds = (await _db.Stores.AsNoTracking()
                .Where(s => s.BrandId == brandId).Select(s => s.Id).ToListAsync(ct)).ToHashSet();
            var brandWarehouseIds = (await _db.Warehouses.AsNoTracking()
                .Where(w => w.BrandId == brandId).Select(w => w.Id).ToListAsync(ct)).ToHashSet();

            users = users.Where(u =>
            {
                var m = u.Membership;
                if (m is null) return _user.IsPlatformAdmin; // unscoped / no-role: platform admins only
                return m.ScopeType switch
                {
                    "platform"  => _user.IsPlatformAdmin,    // platform staff visible to platform admins only
                    "brand"     => m.ScopeId == brandId,
                    "franchise" => m.ScopeId is Guid f && brandFranchiseIds.Contains(f),
                    "store"     => m.ScopeId is Guid s && brandStoreIds.Contains(s),
                    "warehouse" => m.ScopeId is Guid w && brandWarehouseIds.Contains(w),
                    _ => false,
                };
            }).ToList();
        }

        // Scope-name lookups (batch)
        var franchises = await _db.Franchises.AsNoTracking()
            .Select(f => new { f.Id, Name = f.DisplayName ?? f.LegalName }).ToListAsync(ct);
        var stores = await _db.Stores.AsNoTracking().Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var fMap = franchises.ToDictionary(x => x.Id, x => x.Name);
        var sMap = stores.ToDictionary(x => x.Id, x => x.Name);

        string ScopeLabel(string? scopeType, Guid? scopeId) => scopeType switch
        {
            null => "—",
            "platform" or "brand" => "All stores",
            "franchise" => scopeId is Guid sfid && fMap.TryGetValue(sfid, out var fn) ? fn : "Franchise",
            "store" => scopeId is Guid sid && sMap.TryGetValue(sid, out var sn) ? sn : "Store",
            "warehouse" => "Warehouse",
            _ => "—",
        };

        var people = users.Select(u =>
        {
            var name = !string.IsNullOrWhiteSpace(u.Display) ? u.Display!
                     : $"{u.First} {u.Last}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = u.Email ?? "Unknown";
            var roleScope = u.Membership?.RoleScope ?? "brand";
            return new PersonDto(
                u.Id, name, u.Email ?? "", AccessHelpers.Initials(name),
                u.Membership?.RoleCode ?? "—",
                u.Membership?.RoleName ?? "No role",
                ScopeLabel(u.Membership?.ScopeType, u.Membership?.ScopeId),
                AccessHelpers.Tier(roleScope),
                u.Status,
                u.UserType,
                u.LastActiveAt ?? u.LastLoginAt);
        }).ToList();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            people = people.Where(p =>
                p.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                p.Email.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                p.RoleName.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // Sort: explicit column (name/role/active, '-' prefix = descending) or the
        // default tiered order (enterprise first, then most-recently-active).
        people = (q.Sort switch
        {
            "name" => people.OrderBy(p => p.Name),
            "-name" => people.OrderByDescending(p => p.Name),
            "role" => people.OrderBy(p => p.RoleName),
            "-role" => people.OrderByDescending(p => p.RoleName),
            "active" => people.OrderBy(p => p.LastActiveAt ?? DateTimeOffset.MinValue),
            "-active" => people.OrderByDescending(p => p.LastActiveAt ?? DateTimeOffset.MinValue),
            _ => people.OrderByDescending(p => p.Tier == "enterprise")
                       .ThenByDescending(p => p.LastActiveAt ?? DateTimeOffset.MinValue),
        }).ToList();

        var counts = new PeopleCountsDto(
            All: people.Count,
            HqEmployees: people.Count(p => p.Tier == "enterprise"),
            FranchiseOwners: people.Count(p => p.RoleCode == "franchise_owner"),
            FranchiseStaff: people.Count(p => p.Tier == "franchise" && p.RoleCode != "franchise_owner"));

        // Counts reflect the full (search-filtered) set; the list itself is paged.
        var pagedPeople = PaginatedList<PersonDto>.Create(people, q.Page, q.PageSize);
        return new AccessPeoplePageDto(counts, pagedPeople);
    }
}
