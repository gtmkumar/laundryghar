using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Catalog.Queries.Item;

// ── Managed items (item + per-service base prices + fabric variants) ───────────

public sealed record GetManagedItemsQuery(int Page, int PageSize, Guid? ItemGroupId, string? Search)
    : IQuery<PaginatedList<ManagedItemDto>>;

public sealed class GetManagedItemsHandler : IQueryHandler<GetManagedItemsQuery, PaginatedList<ManagedItemDto>>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetManagedItemsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PaginatedList<ManagedItemDto>> HandleAsync(GetManagedItemsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var query = _db.Items.AsNoTracking().Where(x => x.BrandId == brandId);
        if (q.ItemGroupId.HasValue) query = query.Where(x => x.ItemGroupId == q.ItemGroupId.Value);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = $"%{q.Search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, s) || EF.Functions.ILike(x.Code, s));
        }

        var paged = await PaginatedList<laundryghar.SharedDataModel.Entities.CustomerCatalog.Item>.CreateAsync(
            query.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name), q.Page, q.PageSize, ct);

        var itemIds = paged.List.Select(i => i.Id).ToList();

        // Group names for the page.
        var groupNames = await _db.ItemGroups.AsNoTracking()
            .Where(g => g.BrandId == brandId)
            .Select(g => new { g.Id, g.Name })
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        // Per-service base prices from the working list (fabric-null base rows).
        var servicePrices = new Dictionary<Guid, List<ItemServicePriceDto>>();
        var workingId = await WorkingPriceList.ResolveIdAsync(_db, brandId, ct);
        if (workingId is { } wid && itemIds.Count > 0)
        {
            var rows = await _db.PriceListItems.AsNoTracking()
                .Where(pi => pi.PriceListId == wid && pi.IsActive && pi.Status == "active"
                             && pi.FabricTypeId == null && itemIds.Contains(pi.ItemId))
                .Select(pi => new { pi.ItemId, pi.ServiceId, pi.BasePrice })
                .ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.ItemId))
                servicePrices[g.Key] = g
                    .GroupBy(r => r.ServiceId)
                    .Select(sg => new ItemServicePriceDto(sg.Key, sg.First().BasePrice))
                    .ToList();
        }

        // Fabric variants per item.
        var fabricsByItem = new Dictionary<Guid, List<Guid>>();
        if (itemIds.Count > 0)
        {
            var variants = await _db.ItemVariants.AsNoTracking()
                .Where(v => v.BrandId == brandId && v.DeletedAt == null
                            && v.FabricTypeId != null && itemIds.Contains(v.ItemId))
                .Select(v => new { v.ItemId, FabricTypeId = v.FabricTypeId!.Value })
                .ToListAsync(ct);
            foreach (var g in variants.GroupBy(v => v.ItemId))
                fabricsByItem[g.Key] = g.Select(v => v.FabricTypeId).Distinct().ToList();
        }

        return paged.Map(i => new ManagedItemDto(
            i.Id, i.ItemGroupId,
            i.ItemGroupId is { } gid && groupNames.TryGetValue(gid, out var gn) ? gn : null,
            i.Code, i.Name, i.NameLocalized, i.Description, i.TypicalWeightGrams, i.TatHours,
            i.ExpressEligible, i.ExpressSurcharge, i.Aliases, i.DisplayOrder, i.Status, i.UpdatedAt,
            fabricsByItem.TryGetValue(i.Id, out var fids) ? fids : [],
            servicePrices.TryGetValue(i.Id, out var sp) ? sp : [],
            i.PricingMode));
    }
}

// ── Item stats (stat cards) ────────────────────────────────────────────────────

public sealed record GetItemStatsQuery : IQuery<ItemStatsDto>;

public sealed class GetItemStatsHandler : IQueryHandler<GetItemStatsQuery, ItemStatsDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public GetItemStatsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ItemStatsDto> HandleAsync(GetItemStatsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();

        var byStatus = await _db.Items.AsNoTracking()
            .Where(i => i.BrandId == brandId)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total  = byStatus.Sum(x => x.Count);
        var active = byStatus.Where(x => x.Status == "active").Sum(x => x.Count);
        var draft  = byStatus.Where(x => x.Status == "draft").Sum(x => x.Count);

        var categoryCount = await _db.ItemGroups.AsNoTracking()
            .Where(g => g.BrandId == brandId).CountAsync(ct);

        var avgTat = await _db.Items.AsNoTracking()
            .Where(i => i.BrandId == brandId && i.TatHours != null)
            .Select(i => (double?)i.TatHours!.Value).AverageAsync(ct) ?? 0;

        return new ItemStatsDto(total, categoryCount, active, draft, (int)Math.Round(avgTat));
    }
}
