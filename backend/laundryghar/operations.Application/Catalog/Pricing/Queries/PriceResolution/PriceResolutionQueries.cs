using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Commands.PriceListItem;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Queries.PriceResolution;

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
    Guid? StoreId,
    /// <summary>Declared garment value — required for value-slab items (GH #22), ignored otherwise.</summary>
    decimal? DeclaredValue = null
) : IQuery<PriceResolutionDto?>;

public sealed class ResolvePriceHandler : IQueryHandler<ResolvePriceQuery, PriceResolutionDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public ResolvePriceHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PriceResolutionDto?> HandleAsync(ResolvePriceQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        // Delegates to the shared resolver (GH #24 dedupe) and maps to the admin DTO. Value-slab items
        // come back with PriceListId = Guid.Empty and code/scope "value_slab".
        var r = await SharedPriceResolver.ResolveAsync(
            _db, brandId, q.StoreId, q.ItemId, q.ServiceId, q.VariantId, q.DeclaredValue, ct);
        if (r is null) return null;

        return new PriceResolutionDto(
            r.PriceListId,
            r.PriceListCode,
            r.ScopeType,
            r.BasePrice,
            r.ExpressPrice,
            r.TaxRatePercent,
            r.IsTaxable,
            r.DisplayLabel);
    }
}

// ── Customer-facing: published price list ─────────────────────────────────────

public sealed record GetPublishedPriceListQuery() : IQuery<IReadOnlyList<PriceListItemDto>>;

public sealed class GetPublishedPriceListHandler : IQueryHandler<GetPublishedPriceListQuery, IReadOnlyList<PriceListItemDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public GetPublishedPriceListHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<IReadOnlyList<PriceListItemDto>> HandleAsync(GetPublishedPriceListQuery q, CancellationToken ct)
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
                PricingMode = pi.Item.PricingMode,
            })
            .ToListAsync(ct);

        return rows
            .Select(r =>
            {
                var dto = CreatePriceListItemHandler.ToDto(r.Item) with
                {
                    ItemName = r.ItemName,
                    ServiceName = r.ServiceName,
                    PricingMode = r.PricingMode,
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
