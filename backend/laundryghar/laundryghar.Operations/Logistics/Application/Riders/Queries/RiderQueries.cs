using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using laundryghar.Logistics.Application.Riders.Commands;
using laundryghar.Logistics.Application.Riders.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Logistics.Application.Riders.Queries;

// ── List Riders ───────────────────────────────────────────────────────────────

public sealed record GetRidersQuery(
    int     Page,
    int     PageSize,
    string? Status,
    Guid?   FranchiseId,
    string? Search,
    string? KycStatus,
    string? Sort)
    : IRequest<PaginatedList<RiderDto>>;

public sealed class GetRidersHandler : IRequestHandler<GetRidersQuery, PaginatedList<RiderDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRidersHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaginatedList<RiderDto>> Handle(GetRidersQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query   = _db.Riders.Where(r => r.BrandId == brandId);

        // ── Franchise scoping (defense-in-depth) ────────────────────────────
        // If the actor is franchise-scoped, clamp to their own franchise regardless
        // of the franchiseId filter sent in the request — they cannot see others.
        if (_user.FranchiseId is Guid actorFid)
            query = query.Where(r => r.FranchiseId == actorFid);
        else if (q.FranchiseId.HasValue)
            query = query.Where(r => r.FranchiseId == q.FranchiseId.Value);

        if (!string.IsNullOrEmpty(q.Status))
            query = query.Where(r => r.Status == q.Status);

        if (!string.IsNullOrEmpty(q.KycStatus))
            query = query.Where(r => r.KycStatus == q.KycStatus);

        // ── Search — resolve matching userIds first, then OR with RiderCode ─
        // Matches rider_code ILIKE term OR linked user email/phone ILIKE term
        // OR user profile name ILIKE term. Brand-bounded user lookup avoids
        // cross-brand leakage. Single extra round-trip on list paths only.
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            var matchedUserIds = await _db.Users
                .Where(u => u.DeletedAt == null
                    && _db.UserScopeMemberships.Any(m =>
                        m.UserId == u.Id && m.RevokedAt == null
                        && (m.ScopeType == "brand" && m.ScopeId == brandId
                            || m.ScopeType == "franchise" && _db.Franchises.Any(f => f.Id == m.ScopeId && f.BrandId == brandId)
                            || m.ScopeType == "store"     && _db.Stores.Any(s => s.Id == m.ScopeId && s.BrandId == brandId)))
                    && (EF.Functions.ILike(u.Email ?? "",     "%" + term + "%")
                     || EF.Functions.ILike(u.PhoneE164 ?? "", "%" + term + "%")
                     || _db.UserProfiles.Any(p =>
                            p.UserId == u.Id
                            && EF.Functions.ILike((p.FirstName ?? "") + " " + (p.LastName ?? ""), "%" + term + "%"))))
                .Select(u => u.Id)
                .ToListAsync(ct);

            query = query.Where(r =>
                EF.Functions.ILike(r.RiderCode, "%" + term + "%")
                || matchedUserIds.Contains(r.UserId));
        }

        // ── Sorting ──────────────────────────────────────────────────────────
        // Accepted fields: created|kyc|status|name|franchise (default: -created).
        // Unknown fields silently fall back to default — raw input is never
        // interpolated into SQL; all branches are explicit EF expressions.
        var sort      = q.Sort ?? "-created";
        var descending = sort.StartsWith('-');
        var field      = sort.TrimStart('-').ToLowerInvariant();

        IOrderedQueryable<Rider> ordered = field switch
        {
            "kyc"      => descending ? query.OrderByDescending(r => r.KycStatus)   : query.OrderBy(r => r.KycStatus),
            "status"   => descending ? query.OrderByDescending(r => r.Status)      : query.OrderBy(r => r.Status),
            "name"     => descending ? query.OrderByDescending(r => r.RiderCode)   : query.OrderBy(r => r.RiderCode),
            "franchise"=> descending ? query.OrderByDescending(r => r.FranchiseId) : query.OrderBy(r => r.FranchiseId),
            _          => query.OrderByDescending(r => r.CreatedAt), // "created" + fallback
        };
        // When the explicit field is non-default, tiebreak by newest-first.
        if (field is "kyc" or "status" or "name" or "franchise")
            ordered = descending
                ? ordered.ThenByDescending(r => r.CreatedAt)
                : ordered.ThenByDescending(r => r.CreatedAt);

        // Paginate the raw Rider rows in SQL (one count + one range query).
        var page = await PaginatedList<Rider>.CreateAsync(ordered, q.Page, q.PageSize, ct);

        if (page.List.Count == 0)
            return page.Map(_ => default(RiderDto)!); // returns empty page preserving metadata

        // ── Batch lookups — one query per related table, scoped to the page ──
        var userIds      = page.List.Select(r => r.UserId).Distinct().ToList();
        var franchiseIds = page.List.Select(r => r.FranchiseId).Distinct().ToList();
        var storeIds     = page.List.Select(r => r.PrimaryStoreId)
                                    .Where(id => id.HasValue)
                                    .Select(id => id!.Value)
                                    .Distinct()
                                    .ToList();

        var users = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.PhoneE164, u.Status })
            .ToListAsync(ct);

        var profiles = await _db.UserProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new
            {
                p.UserId,
                Name = ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim()
            })
            .ToListAsync(ct);

        var franchises = await _db.Franchises.AsNoTracking()
            .Where(f => franchiseIds.Contains(f.Id))
            .Select(f => new { f.Id, Name = f.DisplayName ?? f.LegalName })
            .ToListAsync(ct);

        // ── In-memory dictionaries for O(1) lookup ────────────────────────────
        var userMap      = users.ToDictionary(u => u.Id);
        var profileMap   = profiles.ToDictionary(p => p.UserId);
        var franchiseMap = franchises.ToDictionary(f => f.Id);

        // Store lookup is built separately so the anonymous type is consistent.
        Dictionary<Guid, string> storeNameMap;
        if (storeIds.Count > 0)
        {
            var stores = await _db.Stores.AsNoTracking()
                .Where(s => storeIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(ct);
            storeNameMap = stores.ToDictionary(s => s.Id, s => s.Name);
        }
        else
        {
            storeNameMap = [];
        }

        return page.Map(r =>
        {
            userMap     .TryGetValue(r.UserId,      out var u);
            profileMap  .TryGetValue(r.UserId,      out var p);
            franchiseMap.TryGetValue(r.FranchiseId, out var f);

            var rawName   = p?.Name;
            var riderName = string.IsNullOrWhiteSpace(rawName) ? null : rawName;
            var storeName = r.PrimaryStoreId.HasValue && storeNameMap.TryGetValue(r.PrimaryStoreId.Value, out var sn)
                                ? sn : null;

            var dto = CreateRiderHandler.ToDto(
                r,
                riderName,
                u?.Email,
                u?.PhoneE164,
                u?.Status,
                f?.Name,
                storeName);
            return RiderDtoFinancialMask.Apply(dto, _user);
        });
    }
}

// ── Get Rider By Id ───────────────────────────────────────────────────────────

public sealed record GetRiderByIdQuery(Guid Id) : IRequest<RiderDto?>;

public sealed class GetRiderByIdHandler : IRequestHandler<GetRiderByIdQuery, RiderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetRiderByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<RiderDto?> Handle(GetRiderByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var rider   = await _db.Riders
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        if (rider is null) return null;

        // Franchise scoping (defense-in-depth): franchise-scoped actors must not
        // read riders that belong to a different franchise.
        if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;

        var dto = await CreateRiderHandler.LoadEnrichedAsync(_db, rider, ct);
        return RiderDtoFinancialMask.Apply(dto, _user);
    }
}
