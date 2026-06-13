using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using laundryghar.Catalog.Application.Pricing.Commands;
using laundryghar.Catalog.Application.Pricing.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Catalog.Application.Pricing.Queries;

// ── PriceList list/get ────────────────────────────────────────────────────────

public sealed record GetPriceListsQuery(int Page, int PageSize) : IRequest<PaginatedList<PriceListDto>>;

public sealed class GetPriceListsHandler : IRequestHandler<GetPriceListsQuery, PaginatedList<PriceListDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPriceListsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PriceListDto>> Handle(GetPriceListsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Brand predicate enforced in-handler (defense-in-depth; RLS also active for non-superuser roles).
        return PaginatedList<PriceListDto>.CreateAsync(
            _db.PriceLists.Where(x => x.BrandId == brandId).OrderByDescending(x => x.CreatedAt)
                .Select(x => CreatePriceListHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

public sealed record GetPriceListByIdQuery(Guid Id) : IRequest<PriceListDto?>;

public sealed class GetPriceListByIdHandler : IRequestHandler<GetPriceListByIdQuery, PriceListDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPriceListByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceListDto?> Handle(GetPriceListByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.PriceLists
            .FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : CreatePriceListHandler.ToDto(e);
    }
}

// ── PriceListItem list/get ────────────────────────────────────────────────────

public sealed record GetPriceListItemsQuery(Guid PriceListId, int Page, int PageSize) : IRequest<PaginatedList<PriceListItemDto>>;

public sealed class GetPriceListItemsHandler : IRequestHandler<GetPriceListItemsQuery, PaginatedList<PriceListItemDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPriceListItemsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PriceListItemDto>> Handle(GetPriceListItemsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        return PaginatedList<PriceListItemDto>.CreateAsync(
            _db.PriceListItems
                // Scope by brand_id + priceListId — prevents reading another brand's items.
                .Where(x => x.PriceListId == q.PriceListId && x.BrandId == brandId)
                .OrderBy(x => x.CreatedAt)
                .Select(x => CreatePriceListItemHandler.ToDto(x)),
            q.Page, q.PageSize, ct);
    }
}

// ── Price Resolution ──────────────────────────────────────────────────────────

/// <summary>
/// Resolves the effective price for a given item×service×variant combination.
/// Price resolution rule (most-specific wins, fallback chain):
///   1. Store-scoped PUBLISHED price list for the caller's store (if storeId provided)
///   2. Franchise-scoped PUBLISHED price list (if franchiseId resolvable from store)
///   3. Brand-scoped PUBLISHED price list (always present for seeded data)
/// Within a scope, the most recently published list wins.
/// Row matching: itemId + serviceId. VariantId filter applied if provided (nullable-match).
/// If no store or franchise price list exists, returns the brand list's price.
/// Returns null if no published price exists at any scope.
/// </summary>
public sealed record ResolvePriceQuery(
    Guid ItemId,
    Guid ServiceId,
    Guid? VariantId,
    Guid? StoreId
) : IRequest<PriceResolutionDto?>;

public sealed class ResolvePriceHandler : IRequestHandler<ResolvePriceQuery, PriceResolutionDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public ResolvePriceHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceResolutionDto?> Handle(ResolvePriceQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        // Load candidate published price lists scoped to the caller's brand (defense-in-depth).
        // Scope priority: store(2) > franchise(1) > brand(0).
        // Within the same priority, most-recently published wins.
        var publishedLists = await _db.PriceLists
            .Where(pl => pl.IsPublished && pl.DeletedAt == null && pl.Status == "published" && pl.BrandId == brandId)
            .Select(pl => new
            {
                pl.Id, pl.Code, pl.ScopeType, pl.FranchiseId, pl.StoreId,
                pl.PublishedAt, pl.IsPublished
            })
            .ToListAsync(ct);

        // Prioritise: store (if storeId given) → franchise → brand
        // Resolve franchise from the store if needed
        Guid? franchiseId = null;
        if (q.StoreId.HasValue)
        {
            var store = await _db.Stores
                .Where(s => s.Id == q.StoreId.Value)
                .Select(s => new { s.FranchiseId })
                .FirstOrDefaultAsync(ct);
            franchiseId = store?.FranchiseId;
        }

        var scopedList = publishedLists
            .Where(pl =>
                (q.StoreId.HasValue     && pl.ScopeType == "store"     && pl.StoreId     == q.StoreId)     ||
                (franchiseId.HasValue   && pl.ScopeType == "franchise"  && pl.FranchiseId == franchiseId) ||
                pl.ScopeType == "brand")
            .OrderByDescending(pl =>
                pl.ScopeType == "store"     ? 2 :
                pl.ScopeType == "franchise" ? 1 : 0)
            .ThenByDescending(pl => pl.PublishedAt)
            .Select(pl => pl.Id)
            .ToList();

        foreach (var listId in scopedList)
        {
            // Match the most-specific price row: prefer variant-specific, fall back to non-variant
            var priceItem = await _db.PriceListItems
                .Where(pi =>
                    pi.PriceListId == listId &&
                    pi.ItemId     == q.ItemId &&
                    pi.ServiceId  == q.ServiceId &&
                    pi.IsActive   &&
                    pi.Status     == "active" &&
                    (q.VariantId == null ? pi.ItemVariantId == null
                                        : (pi.ItemVariantId == q.VariantId || pi.ItemVariantId == null)))
                .OrderByDescending(pi => pi.ItemVariantId != null ? 1 : 0) // variant-specific first
                .Select(pi => new { pi.Id, pi.BasePrice, pi.ExpressPrice, pi.TaxRatePercent, pi.IsTaxable, pi.DisplayLabel })
                .FirstOrDefaultAsync(ct);

            if (priceItem is null) continue;

            var list = publishedLists.First(pl => pl.Id == listId);
            return new PriceResolutionDto(
                listId,
                list.Code,
                list.ScopeType,
                priceItem.BasePrice,
                priceItem.ExpressPrice,
                priceItem.TaxRatePercent,
                priceItem.IsTaxable,
                priceItem.DisplayLabel);
        }

        return null; // no published price found at any scope
    }
}

// ── Customer-facing: published price list ─────────────────────────────────────

public sealed record GetPublishedPriceListQuery() : IRequest<IReadOnlyList<PriceListItemDto>>;

public sealed class GetPublishedPriceListHandler : IRequestHandler<GetPublishedPriceListQuery, IReadOnlyList<PriceListItemDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPublishedPriceListHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<PriceListItemDto>> Handle(GetPublishedPriceListQuery q, CancellationToken ct)
    {
        var brandId = _user.BrandId
            ?? throw new UnauthorizedAccessException("Customer brand context required.");
        // Brand predicate ensures a customer never sees another brand's pricing.
        // This is enforced in-handler (defense-in-depth on top of RLS).
        var priceList = await _db.PriceLists
            .Where(pl => pl.IsPublished && pl.ScopeType == "brand" && pl.Status == "published"
                         && pl.DeletedAt == null && pl.BrandId == brandId)
            .OrderByDescending(pl => pl.PublishedAt)
            .FirstOrDefaultAsync(ct);

        if (priceList is null) return [];

        // DEFECT 1: project the item + service names alongside the row so the client
        // gets a real display label. Admin authoring may leave DisplayLabel null; we
        // synthesise "Item · Service" in that case. The join uses the Item/Service
        // navigations (translated to SQL JOINs — single round-trip, no N+1).
        var rows = await _db.PriceListItems
            .Where(pi => pi.PriceListId == priceList.Id && pi.IsActive && pi.Status == "active")
            .OrderBy(pi => pi.CreatedAt)
            .Select(pi => new
            {
                Item = pi,
                ItemName = pi.Item.Name,
                ServiceName = pi.Service.Name,
            })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var dto = CreatePriceListItemHandler.ToDto(r.Item) with
                {
                    ItemName = r.ItemName,
                    ServiceName = r.ServiceName,
                };
                // Fill a sensible label when the authored one is blank.
                var label = string.IsNullOrWhiteSpace(dto.DisplayLabel)
                    ? BuildLabel(r.ItemName, r.ServiceName)
                    : dto.DisplayLabel;
                return dto with { DisplayLabel = label };
            })
            .ToList();
    }

    /// <summary>Composes a "Item · Service" label, omitting blanks gracefully.</summary>
    internal static string BuildLabel(string? itemName, string? serviceName)
    {
        var parts = new[] { itemName, serviceName }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var label = string.Join(" · ", parts); // middle dot separator
        return string.IsNullOrWhiteSpace(label) ? "Item" : label;
    }
}
