using System.Text.Json;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Common.Interfaces;
using ItemEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.Item;
using PliEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceListItem;

namespace operations.Application.Catalog.Catalog.Commands.Item;

/// <summary>
/// Bulk import items from CSV. Each row upserts an item by code (create or update)
/// and sets its per-service base prices in the working list. Services match by name,
/// category by item-group name or code. Returns a created/updated/errors summary.
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
        var errors = new List<string>();
        int created = 0, updated = 0, pricesSet = 0;

        if (rows.Count == 0)
            return new ImportItemsResult(0, 0, 0, ["No rows to import."]);

        var serviceByName = await _db.Services.AsNoTracking()
            .Where(s => s.BrandId == brandId)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);
        var svcLookup = serviceByName
            .GroupBy(s => s.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Id);

        var groups = await _db.ItemGroups.AsNoTracking()
            .Where(g => g.BrandId == brandId)
            .Select(g => new { g.Id, g.Name, g.Code })
            .ToListAsync(ct);

        Guid? ResolveGroup(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            var c = category.Trim().ToLowerInvariant();
            return groups.FirstOrDefault(g => g.Name.ToLowerInvariant() == c || g.Code.ToLowerInvariant() == c)?.Id;
        }

        var working = await WorkingPriceList.EnsureAsync(_db, brandId, cmd.ActorId, ct);

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

            var baseRows = await _db.PriceListItems
                .Where(pi => pi.PriceListId == working.Id && pi.ItemId == item.Id && pi.FabricTypeId == null)
                .ToListAsync(ct);

            foreach (var sp in row.ServicePrices ?? [])
            {
                if (string.IsNullOrWhiteSpace(sp.ServiceName)) continue;
                if (!svcLookup.TryGetValue(sp.ServiceName.Trim().ToLowerInvariant(), out var serviceId))
                {
                    errors.Add($"Row {lineNo}: service '{sp.ServiceName}' not found — price skipped.");
                    continue;
                }

                var existing = baseRows.FirstOrDefault(r => r.ServiceId == serviceId);
                if (sp.BasePrice is null)
                {
                    if (existing is { IsActive: true }) { existing.IsActive = false; existing.UpdatedAt = now; existing.UpdatedBy = cmd.ActorId; }
                    continue;
                }

                if (existing is null)
                {
                    var pli = new PliEntity
                    {
                        Id = Guid.NewGuid(), PriceListId = working.Id, BrandId = brandId,
                        ServiceId = serviceId, ItemId = item.Id, ItemGroupId = item.ItemGroupId,
                        BasePrice = sp.BasePrice.Value, MinimumQuantity = 1, TaxRatePercent = 0, IsTaxable = false,
                        DisplayLabel = $"{item.Name} – {sp.ServiceName.Trim()}", IsActive = true, Status = "active",
                        CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
                    };
                    _db.PriceListItems.Add(pli);
                    baseRows.Add(pli);
                }
                else
                {
                    existing.BasePrice = sp.BasePrice.Value;
                    existing.IsActive  = true;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = cmd.ActorId;
                }
                pricesSet++;
            }
        }

        if (created > 0 || updated > 0)
            PricingChangeLogger.Add(_db, brandId, "price_list_item", working.Id,
                $"CSV import: {created} created, {updated} updated, {pricesSet} prices set",
                new { }, new { created, updated, pricesSet }, cmd.ActorId, _user.Email);

        await _db.SaveChangesAsync(ct);
        return new ImportItemsResult(created, updated, pricesSet, errors);
    }
}
