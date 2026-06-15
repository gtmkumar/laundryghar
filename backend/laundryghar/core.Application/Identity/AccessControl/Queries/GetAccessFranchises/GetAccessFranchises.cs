using System.Text.Json;
using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Queries.GetAccessFranchises;

public sealed record GetAccessFranchisesQuery(int Page, int PageSize, string? Search = null)
    : IQuery<PaginatedList<FranchiseCardDto>>;

public class GetAccessFranchisesQueryHandler : IQueryHandler<GetAccessFranchisesQuery, PaginatedList<FranchiseCardDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public GetAccessFranchisesQueryHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    // Raw page row materialised from SQL before the in-memory card mapping.
    private sealed record Row(Guid Id, string Name, string OnboardingStatus, string Status, string Metadata, Guid? OwnerUserId, int StoreCount, int StaffCount, int RiderCount);

    public async Task<PaginatedList<FranchiseCardDto>> HandleAsync(GetAccessFranchisesQuery q, CancellationToken ct)
    {
        var brandId = _user.BrandId; // null for platform_admin (resolves all under RLS bypass)

        var baseQuery = _db.Franchises.AsNoTracking().Where(f => f.DeletedAt == null);
        if (brandId.HasValue) baseQuery = baseQuery.Where(f => f.BrandId == brandId.Value);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = $"%{q.Search.Trim()}%";
            baseQuery = baseQuery.Where(f =>
                EF.Functions.ILike(f.DisplayName ?? f.LegalName, pattern) || EF.Functions.ILike(f.Code, pattern));
        }

        // Newest activity first: UpdatedAt is touched on every edit, CreatedAt breaks ties.
        // Ordering happens in SQL before Skip/Take so paging is stable.
        var ordered = baseQuery
            .OrderByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.CreatedAt)
            .Select(f => new Row(
                f.Id, f.DisplayName ?? f.LegalName, f.OnboardingStatus, f.Status, f.Metadata, f.OwnerUserId,
                _db.Stores.Count(s => s.FranchiseId == f.Id && s.DeletedAt == null),
                // Staff: users whose PRIMARY membership is scoped to this franchise (or a store
                // within it), excluding the owner and riders — same definition the team drawer uses.
                _db.Users.Count(u => u.Status != "deleted" && u.UserType != "rider"
                    && u.ScopeMemberships
                        .Where(m => m.RevokedAt == null)
                        .OrderByDescending(m => m.IsPrimary)
                        .Take(1)
                        .Any(m => m.Role.Code != "franchise_owner"
                            && ((m.ScopeType == "franchise" && m.ScopeId == f.Id)
                                || (m.ScopeType == "store" && _db.Stores.Any(s => s.Id == m.ScopeId && s.FranchiseId == f.Id && s.DeletedAt == null))))),
                _db.Riders.Count(rd => rd.FranchiseId == f.Id && rd.DeletedAt == null)));

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
            int sinceYear = 0; long rev = 0; string location = "—", ownership = "franchise";
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(r.Metadata) ? "{}" : r.Metadata);
                var root = doc.RootElement;
                if (root.TryGetProperty("sinceYear", out var sy) && sy.TryGetInt32(out var syi)) sinceYear = syi;
                if (root.TryGetProperty("revenueMonthly", out var rm) && rm.TryGetInt64(out var rmi)) rev = rmi;
                if (root.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.String) location = loc.GetString()!;
                if (root.TryGetProperty("ownershipType", out var ot) && ot.ValueKind == JsonValueKind.String) ownership = ot.GetString()!;
            }
            catch { /* malformed metadata → defaults */ }

            var ownerName = r.OwnerUserId is Guid oid && ownerMap.TryGetValue(oid, out var on) && !string.IsNullOrWhiteSpace(on) ? on : null;
            var status = r.OnboardingStatus is "onboarding" or "pending" or "in_progress" or "setup" or "draft"
                ? "Onboarding" : "Active";

            // staff/rider counts are now REAL (from SQL), not seeded metadata — so the
            // tile matches the hovercard/drawer. storeCount was already real.
            return new FranchiseCardDto(
                r.Id, r.Name, ownership, location, sinceYear,
                ownerName, ownerName is null ? null : AccessHelpers.Initials(ownerName),
                r.StoreCount, r.StaffCount, r.RiderCount, rev, status);
        });
    }
}
