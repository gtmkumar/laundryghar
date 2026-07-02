using System.Text.Json;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Import;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Common.Interfaces;
using GroupEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.ItemGroup;
using ItemEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.Item;
using PliEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceListItem;

namespace operations.Application.Catalog.Catalog.Commands.Item;

/// <summary>
/// Bulk import items from a parsed CSV/XLSX. Each row upserts an item by code (create or update) and
/// sets its per-service base prices — and optional per-fabric prices — in a target price list. Services
/// match by name, category by item-group name or code, fabrics by name. The target defaults to the
/// brand working list; <see cref="ImportOptions.TargetPriceListId"/> may redirect writes to an
/// unpublished list. <see cref="ImportOptions.AutoCreateCategories"/> creates missing item-groups first.
/// Returns a created/updated/errors summary.
/// </summary>
public sealed record ImportItemsCommand(ImportItemsRequest Request, Guid? ActorId) : ICommand<ImportItemsResult>;

public sealed class ImportItemsHandler : ICommandHandler<ImportItemsCommand, ImportItemsResult>
{
    private static readonly HashSet<string> AllowedStatus = ["active", "draft", "archived", "disabled", "seasonal"];

    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public ImportItemsHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<ImportItemsResult> HandleAsync(ImportItemsCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var now = DateTimeOffset.UtcNow;
        var rows = cmd.Request.Rows ?? [];
        var options = cmd.Request.Options;
        var errors = new List<string>();
        int created = 0, updated = 0, pricesSet = 0, categoriesCreated = 0;

        if (rows.Count == 0)
            return new ImportItemsResult(0, 0, 0, ["No rows to import."]);

        var serviceByName = await _db.Services.AsNoTracking()
            .Where(s => s.BrandId == brandId)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);
        var svcLookup = serviceByName
            .GroupBy(s => s.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var fabricByName = await _db.FabricTypes.AsNoTracking()
            .Where(f => f.BrandId == brandId && f.DeletedAt == null)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(ct);
        var fabricLookup = fabricByName
            .GroupBy(f => f.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Item-group lookup keyed by both name and code; mutable so auto-created groups resolve too.
        var groupList = await _db.ItemGroups.AsNoTracking()
            .Where(g => g.BrandId == brandId && g.DeletedAt == null)
            .Select(g => new { g.Id, g.Name, g.Code })
            .ToListAsync(ct);
        var groupLookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groupList)
        {
            groupLookup.TryAdd(g.Name.Trim(), g.Id);
            groupLookup.TryAdd(g.Code.Trim(), g.Id);
        }

        Guid? ResolveGroup(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            return groupLookup.TryGetValue(category.Trim(), out var id) ? id : null;
        }

        // Auto-create missing categories up-front so item rows can resolve them.
        if (options?.AutoCreateCategories == true)
        {
            var missing = rows
                .Select(r => r.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(c => !groupLookup.ContainsKey(c))
                .ToList();

            short groupOrder = (short)(groupList.Count + 1);
            foreach (var cat in missing)
            {
                var group = new GroupEntity
                {
                    Id            = Guid.NewGuid(),
                    BrandId       = brandId,
                    Code          = ImportFileParser.SlugifyCode(cat),
                    Name          = cat,
                    NameLocalized = $"{{\"en\":{JsonSerializer.Serialize(cat)}}}",
                    DisplayOrder  = groupOrder++,
                    IsVisibleMobile = true,
                    Status        = "active",
                    CreatedAt     = now,
                    UpdatedAt     = now,
                    CreatedBy     = cmd.ActorId,
                    UpdatedBy     = cmd.ActorId,
                };
                _db.ItemGroups.Add(group);
                groupLookup[cat] = group.Id;
                groupLookup.TryAdd(group.Code.Trim(), group.Id);
                categoriesCreated++;
            }
        }

        // Resolve the target price list: default working list, or a caller-specified unpublished list.
        var target = await ResolveTargetListAsync(options?.TargetPriceListId, brandId, cmd.ActorId, ct);

        for (var idx = 0; idx < rows.Count; idx++)
        {
            var row = rows[idx];
            var lineNo = idx + 2; // header is line 1
            if (string.IsNullOrWhiteSpace(row.Code) || string.IsNullOrWhiteSpace(row.Name))
            {
                errors.Add($"Row {lineNo}: code and name are required — skipped.");
                continue;
            }

            var code = row.Code.Trim();
            var status = !string.IsNullOrWhiteSpace(row.Status) && AllowedStatus.Contains(row.Status.Trim().ToLowerInvariant())
                ? row.Status.Trim().ToLowerInvariant() : "active";
            var groupId = ResolveGroup(row.Category);
            var nameLocalized = $"{{\"en\":{JsonSerializer.Serialize(row.Name.Trim())}}}";

            var item = await _db.Items.FirstOrDefaultAsync(i => i.BrandId == brandId && i.Code == code, ct);
            if (item is null)
            {
                item = new ItemEntity
                {
                    Id            = Guid.NewGuid(),
                    BrandId       = brandId,
                    ItemGroupId   = groupId,
                    Code          = code,
                    Name          = row.Name.Trim(),
                    NameLocalized = nameLocalized,
                    TatHours      = row.TatHours,
                    Aliases       = [],
                    DisplayOrder  = 0,
                    Status        = status,
                    CreatedAt     = now,
                    UpdatedAt     = now,
                    CreatedBy     = cmd.ActorId,
                    UpdatedBy     = cmd.ActorId,
                    Version       = 1,
                };
                _db.Items.Add(item);
                created++;
            }
            else if (item.DeletedAt != null)
            {
                errors.Add($"Row {lineNo}: item '{code}' is deleted — skipped.");
                continue;
            }
            else
            {
                item.Name        = row.Name.Trim();
                item.ItemGroupId = groupId ?? item.ItemGroupId;
                item.TatHours    = row.TatHours ?? item.TatHours;
                item.Status      = status;
                item.UpdatedAt   = now;
                item.UpdatedBy   = cmd.ActorId;
                item.Version++;
                updated++;
            }

            // Row-level tax: applied to the price rows this row touches. Absent → leave tax as-is.
            var applyTax = row.TaxRatePercent is not null;
            var taxRate = row.TaxRatePercent ?? 0m;
            var isTaxable = taxRate > 0m;

            // All (base + fabric) price rows for this item in the target list, matched by (service, fabric).
            var priceRows = await _db.PriceListItems
                .Where(pi => pi.PriceListId == target.Id && pi.ItemId == item.Id)
                .ToListAsync(ct);

            void Upsert(Guid serviceId, Guid? fabricTypeId, decimal price, string label)
            {
                var existing = priceRows.FirstOrDefault(r => r.ServiceId == serviceId && r.FabricTypeId == fabricTypeId);
                if (existing is null)
                {
                    var pli = new PliEntity
                    {
                        Id = Guid.NewGuid(), PriceListId = target.Id, BrandId = brandId,
                        ServiceId = serviceId, ItemId = item.Id, ItemGroupId = item.ItemGroupId,
                        FabricTypeId = fabricTypeId,
                        BasePrice = price, MinimumQuantity = 1,
                        TaxRatePercent = taxRate, IsTaxable = applyTax && isTaxable,
                        DisplayLabel = label, IsActive = true, Status = "active",
                        CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
                    };
                    _db.PriceListItems.Add(pli);
                    priceRows.Add(pli);
                }
                else
                {
                    existing.BasePrice = price;
                    existing.IsActive  = true;
                    if (applyTax) { existing.TaxRatePercent = taxRate; existing.IsTaxable = isTaxable; }
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = cmd.ActorId;
                }
                pricesSet++;
            }

            foreach (var sp in row.ServicePrices ?? [])
            {
                if (string.IsNullOrWhiteSpace(sp.ServiceName)) continue;
                if (!svcLookup.TryGetValue(sp.ServiceName.Trim().ToLowerInvariant(), out var serviceId))
                {
                    errors.Add($"Row {lineNo}: service '{sp.ServiceName}' not found — price skipped.");
                    continue;
                }

                // Base (fabric-null) price. Null price deactivates the existing base row.
                if (sp.BasePrice is null)
                {
                    var existingBase = priceRows.FirstOrDefault(r => r.ServiceId == serviceId && r.FabricTypeId == null);
                    if (existingBase is { IsActive: true }) { existingBase.IsActive = false; existingBase.UpdatedAt = now; existingBase.UpdatedBy = cmd.ActorId; }
                }
                else
                {
                    Upsert(serviceId, null, sp.BasePrice.Value, $"{item.Name} – {sp.ServiceName.Trim()}");
                }

                // Per-fabric prices. Unknown fabric → error, skip that price.
                foreach (var fp in sp.FabricPrices ?? [])
                {
                    if (!fabricLookup.TryGetValue(fp.FabricName.Trim().ToLowerInvariant(), out var fabricId))
                    {
                        errors.Add($"Row {lineNo}: fabric '{fp.FabricName}' not found — price skipped.");
                        continue;
                    }
                    Upsert(serviceId, fabricId, fp.Price, $"{item.Name} – {sp.ServiceName.Trim()} ({fp.FabricName.Trim()})");
                }
            }
        }

        if (created > 0 || updated > 0)
            PricingChangeLogger.Add(_db, brandId, "price_list_item", target.Id,
                $"Import: {created} created, {updated} updated, {pricesSet} prices set, {categoriesCreated} categories created",
                new { }, new { created, updated, pricesSet, categoriesCreated, fileRef = options?.FileRef }, cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);
        return new ImportItemsResult(created, updated, pricesSet, errors, categoriesCreated);
    }

    /// <summary>
    /// Resolves the price list writes target to. With no id, the brand working list (created if absent).
    /// With an id: it must exist, belong to the brand, be unpublished, and lie within the caller's scope.
    /// </summary>
    private async Task<laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceList> ResolveTargetListAsync(
        Guid? targetPriceListId, Guid brandId, Guid? actorId, CancellationToken ct)
    {
        if (targetPriceListId is not { } id)
            return await WorkingPriceList.EnsureAsync(_db, brandId, actorId, ct);

        var list = await _db.PriceLists.FirstOrDefaultAsync(pl => pl.Id == id && pl.DeletedAt == null, ct);
        if (list is null || list.BrandId != brandId)
            throw new BusinessRuleException("Target price list not found.");
        if (list.IsPublished)
            throw new BusinessRuleException("Cannot import into a published price list. Import into a draft, then publish.");
        if (!_user.IsWithinScope(brandId: list.BrandId, franchiseId: list.FranchiseId, storeId: list.StoreId))
            throw new BusinessRuleException("You are not permitted to modify this price list.");
        return list;
    }
}
