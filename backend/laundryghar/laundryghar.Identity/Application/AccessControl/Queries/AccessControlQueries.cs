using System.Text.Json;
using laundryghar.Identity.Application.AccessControl.Dtos;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Identity.Application.AccessControl.Queries;

internal static class AccessHelpers
{
    public static string Tier(string roleScopeType) =>
        roleScopeType is "platform" or "brand" ? "enterprise" : "franchise";

    public static string Initials(string name)
    {
        var parts = name.Split([' ', '.', '@'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var first = parts[0][..1];
        var second = parts.Length > 1 ? parts[1][..1] : "";
        return (first + second).ToUpperInvariant();
    }
}

// ── People ──────────────────────────────────────────────────────────────────
public sealed record GetAccessPeopleQuery(string? Search, int Page, int PageSize) : IRequest<AccessPeoplePageDto>;

public sealed class GetAccessPeopleHandler : IRequestHandler<GetAccessPeopleQuery, AccessPeoplePageDto>
{
    private readonly LaundryGharDbContext _db;
    public GetAccessPeopleHandler(LaundryGharDbContext db) => _db = db;

    public async Task<AccessPeoplePageDto> Handle(GetAccessPeopleQuery q, CancellationToken ct)
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
            "franchise" => scopeId is Guid fid && fMap.TryGetValue(fid, out var fn) ? fn : "Franchise",
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

        people = people.OrderByDescending(p => p.Tier == "enterprise")
                       .ThenByDescending(p => p.LastActiveAt ?? DateTimeOffset.MinValue)
                       .ToList();

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

// ── Roles & Permissions ─────────────────────────────────────────────────────
public sealed record GetAccessRolesQuery : IRequest<AccessRolesDto>;

public sealed class GetAccessRolesHandler : IRequestHandler<GetAccessRolesQuery, AccessRolesDto>
{
    private readonly LaundryGharDbContext _db;
    public GetAccessRolesHandler(LaundryGharDbContext db) => _db = db;

    public async Task<AccessRolesDto> Handle(GetAccessRolesQuery q, CancellationToken ct)
    {
        var matrix = await ModuleMatrix.LoadAsync(_db, ct);

        var roles = await _db.Roles.AsNoTracking()
            .Where(r => r.DeletedAt == null && r.Status == "active")
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.ScopeType,
                r.IsSystem,
                r.Priority,
                PermCodes = r.RolePermissions.Select(rp => rp.Permission.Code).ToList(),
                MemberCount = r.UserScopeMemberships.Count(m => m.RevokedAt == null),
            })
            .ToListAsync(ct);

        var summaries = roles.Select(r =>
        {
            var onCells = new HashSet<string>();
            foreach (var code in r.PermCodes)
                foreach (var cell in matrix.CellsFor(PermissionMatrix.Module(code), PermissionMatrix.Action(code)))
                    onCells.Add(cell);
            return new
            {
                r.ScopeType,
                Dto = new RoleSummaryDto(r.Id, r.Code, r.Name, r.Description, r.ScopeType, r.IsSystem,
                    r.MemberCount, onCells.OrderBy(c => c).ToList()),
                r.Priority,
            };
        }).ToList();

        RoleGroupDto Group(string tier, string label) =>
            new(tier, label, summaries
                .Where(s => AccessHelpers.Tier(s.ScopeType) == tier)
                .OrderByDescending(s => s.Priority)
                .ThenBy(s => s.Dto.Name)
                .Select(s => s.Dto).ToList());

        var groups = new List<RoleGroupDto> { Group("enterprise", "Enterprise · HQ"), Group("franchise", "Franchise") };

        return new AccessRolesDto(
            matrix.Rows.Select(m => new MatrixModuleDto(m.Key, m.Label)).ToList(),
            PermissionMatrix.Actions.ToList(),
            groups);
    }
}

// ── Navigator (per-user sidebar menu) ───────────────────────────────────────
public sealed record GetNavigatorQuery : IRequest<NavigatorDto>;

public sealed class GetNavigatorHandler : IRequestHandler<GetNavigatorQuery, NavigatorDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetNavigatorHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<NavigatorDto> Handle(GetNavigatorQuery q, CancellationToken ct)
    {
        var mods = await _db.Modules.AsNoTracking()
            .Where(m => m.ShowInNav && m.Status == "active")
            .OrderBy(m => m.NavOrder)
            .Select(m => new { m.Key, m.Label, m.Icon, m.Route, m.Section, m.RequiredPermission })
            .ToListAsync(ct);

        // Gate each item by the signed-in user's permissions (platform_admin sees all).
        var visible = mods.Where(m =>
            string.IsNullOrEmpty(m.RequiredPermission)
            || _user.IsPlatformAdmin
            || _user.HasPermission(m.RequiredPermission));

        var sections = visible
            .GroupBy(m => m.Section ?? "General")
            .Select(g => new NavSectionDto(g.Key,
                g.Select(m => new NavItemDto(m.Key, m.Label, m.Icon, m.Route)).ToList()))
            .ToList();

        return new NavigatorDto(sections);
    }
}

// ── Franchises ──────────────────────────────────────────────────────────────
public sealed record GetAccessFranchisesQuery(int Page, int PageSize) : IRequest<PaginatedList<FranchiseCardDto>>;

public sealed class GetAccessFranchisesHandler : IRequestHandler<GetAccessFranchisesQuery, PaginatedList<FranchiseCardDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetAccessFranchisesHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    // Raw page row materialised from SQL before the in-memory card mapping.
    private sealed record Row(Guid Id, string Name, string OnboardingStatus, string Status, string Metadata, Guid? OwnerUserId, int StoreCount);

    public async Task<PaginatedList<FranchiseCardDto>> Handle(GetAccessFranchisesQuery q, CancellationToken ct)
    {
        var brandId = _user.BrandId; // null for platform_admin (resolves all under RLS bypass)

        var baseQuery = _db.Franchises.AsNoTracking().Where(f => f.DeletedAt == null);
        if (brandId.HasValue) baseQuery = baseQuery.Where(f => f.BrandId == brandId.Value);

        // Newest activity first: UpdatedAt is touched on every edit, CreatedAt breaks ties.
        // Ordering happens in SQL before Skip/Take so paging is stable.
        var ordered = baseQuery
            .OrderByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.CreatedAt)
            .Select(f => new Row(
                f.Id, f.DisplayName ?? f.LegalName, f.OnboardingStatus, f.Status, f.Metadata, f.OwnerUserId,
                _db.Stores.Count(s => s.FranchiseId == f.Id && s.DeletedAt == null)));

        var page = await PaginatedList<Row>.CreateAsync(ordered, q.Page, q.PageSize, ct);

        // Owner names — only for the rows on this page.
        var ownerIds = page.List.Where(r => r.OwnerUserId != null).Select(r => r.OwnerUserId!.Value).Distinct().ToList();
        var owners = await _db.Users.AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = (u.Profile != null ? (u.Profile.DisplayName ?? (u.Profile.FirstName + " " + u.Profile.LastName)) : u.Email) })
            .ToListAsync(ct);
        var ownerMap = owners.ToDictionary(o => o.Id, o => (o.Name ?? "").Trim());

        return page.Map(r =>
        {
            int sinceYear = 0, staff = 0, rider = 0; long rev = 0; string location = "—", ownership = "franchise";
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(r.Metadata) ? "{}" : r.Metadata);
                var root = doc.RootElement;
                if (root.TryGetProperty("sinceYear", out var sy) && sy.TryGetInt32(out var syi)) sinceYear = syi;
                if (root.TryGetProperty("staffCount", out var sc) && sc.TryGetInt32(out var sci)) staff = sci;
                if (root.TryGetProperty("riderCount", out var rc) && rc.TryGetInt32(out var rci)) rider = rci;
                if (root.TryGetProperty("revenueMonthly", out var rm) && rm.TryGetInt64(out var rmi)) rev = rmi;
                if (root.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.String) location = loc.GetString()!;
                if (root.TryGetProperty("ownershipType", out var ot) && ot.ValueKind == JsonValueKind.String) ownership = ot.GetString()!;
            }
            catch { /* malformed metadata → defaults */ }

            var ownerName = r.OwnerUserId is Guid oid && ownerMap.TryGetValue(oid, out var on) && !string.IsNullOrWhiteSpace(on) ? on : null;
            var status = r.OnboardingStatus is "onboarding" or "pending" or "in_progress" or "setup" or "draft"
                ? "Onboarding" : "Active";

            return new FranchiseCardDto(
                r.Id, r.Name, ownership, location, sinceYear,
                ownerName, ownerName is null ? null : AccessHelpers.Initials(ownerName),
                r.StoreCount, staff, rider, rev, status);
        });
    }
}
