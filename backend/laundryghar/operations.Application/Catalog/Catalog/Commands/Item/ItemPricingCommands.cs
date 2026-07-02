using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Common.Interfaces;
using PliEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.PriceListItem;
using VariantEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.ItemVariant;

namespace operations.Application.Catalog.Catalog.Commands.Item;

/// <summary>
/// Saves an item's per-service base prices + fabric variants from the Items screen.
/// Base prices upsert into the brand working list (fabric-null rows). Each per-service
/// change is audited to the pricing change log. Fabric ids replace the item's fabric set.
/// </summary>
public sealed record SaveItemPricingCommand(Guid ItemId, SaveItemPricingRequest Request, Guid? ActorId)
    : ICommand<bool>;

public sealed class SaveItemPricingHandler : ICommandHandler<SaveItemPricingCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public SaveItemPricingHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(SaveItemPricingCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var now = DateTimeOffset.UtcNow;

        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == cmd.ItemId && i.BrandId == brandId, ct);
        if (item is null || item.DeletedAt != null) return false;

        var working = await WorkingPriceList.EnsureAsync(_db, brandId, cmd.ActorId, ct);

        var serviceNames = await _db.Services.AsNoTracking()
            .Where(s => s.BrandId == brandId)
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        // ── Per-service base prices ───────────────────────────────────────────
        var baseRows = await _db.PriceListItems
            .Where(pi => pi.PriceListId == working.Id && pi.ItemId == cmd.ItemId && pi.FabricTypeId == null)
            .ToListAsync(ct);

        foreach (var sp in cmd.Request.ServicePrices ?? [])
        {
            var existing = baseRows.FirstOrDefault(r => r.ServiceId == sp.ServiceId);
            var svcName  = serviceNames.TryGetValue(sp.ServiceId, out var n) ? n : "Service";
            var label    = $"{item.Name} – {svcName}";

            if (sp.BasePrice is null)
            {
                // Cleared → deactivate the row (no hard delete; keeps history sane).
                if (existing is { IsActive: true })
                {
                    var before = SnapshotPli(existing);
                    existing.IsActive  = false;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = cmd.ActorId;
                    PricingChangeLogger.Add(_db, brandId, "price_list_item", existing.Id,
                        $"{label}: removed", before, SnapshotPli(existing), cmd.ActorId, _user.Email);
                }
                continue;
            }

            if (existing is null)
            {
                var created = new PliEntity
                {
                    Id              = Guid.NewGuid(),
                    PriceListId     = working.Id,
                    BrandId         = brandId,
                    ServiceId       = sp.ServiceId,
                    ItemId          = cmd.ItemId,
                    ItemGroupId     = item.ItemGroupId,
                    BasePrice       = sp.BasePrice.Value,
                    MinimumQuantity = 1,
                    TaxRatePercent  = 0,
                    IsTaxable       = false,
                    DisplayLabel    = label,
                    IsActive        = true,
                    Status          = "active",
                    CreatedAt       = now,
                    UpdatedAt       = now,
                    CreatedBy       = cmd.ActorId,
                    UpdatedBy       = cmd.ActorId,
                };
                _db.PriceListItems.Add(created);
                PricingChangeLogger.Add(_db, brandId, "price_list_item", created.Id,
                    $"{label}: base ₹{sp.BasePrice.Value:0.##} (added)", new { }, SnapshotPli(created), cmd.ActorId, _user.Email);
            }
            else
            {
                var before  = SnapshotPli(existing);
                var oldBase = existing.BasePrice;
                existing.BasePrice = sp.BasePrice.Value;
                existing.IsActive  = true;
                existing.DisplayLabel = string.IsNullOrWhiteSpace(existing.DisplayLabel) ? label : existing.DisplayLabel;
                existing.UpdatedAt = now;
                existing.UpdatedBy = cmd.ActorId;
                if (oldBase != existing.BasePrice)
                    PricingChangeLogger.Add(_db, brandId, "price_list_item", existing.Id,
                        $"{label}: base ₹{oldBase:0.##} → ₹{existing.BasePrice:0.##}", before, SnapshotPli(existing), cmd.ActorId, _user.Email);
            }
        }

        // ── Fabric variants (replace the item's fabric set) ───────────────────
        // Null means "leave the fabric set unchanged" — single-cell price edits
        // (e.g. from the price matrix) send only ServicePrices. Only an explicit
        // (possibly empty) list replaces the set.
        if (cmd.Request.FabricTypeIds is null)
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }

        var desired = cmd.Request.FabricTypeIds.Distinct().ToHashSet();
        var allVariants = await _db.ItemVariants
            .Where(v => v.BrandId == brandId && v.ItemId == cmd.ItemId && v.FabricTypeId != null)
            .ToListAsync(ct);

        var fabricMeta = await _db.FabricTypes.AsNoTracking()
            .Where(f => f.BrandId == brandId)
            .Select(f => new { f.Id, f.Code, f.Name })
            .ToDictionaryAsync(f => f.Id, f => new { f.Code, f.Name }, ct);

        foreach (var v in allVariants)
        {
            var fid = v.FabricTypeId!.Value;
            if (desired.Contains(fid))
            {
                if (v.DeletedAt != null) { v.DeletedAt = null; v.Status = "active"; v.UpdatedAt = now; v.UpdatedBy = cmd.ActorId; }
                desired.Remove(fid); // already present
            }
            else if (v.DeletedAt == null)
            {
                v.DeletedAt = now; v.Status = "disabled"; v.UpdatedAt = now; v.UpdatedBy = cmd.ActorId;
            }
        }

        foreach (var fid in desired)
        {
            var meta = fabricMeta.TryGetValue(fid, out var m) ? m : null;
            _db.ItemVariants.Add(new VariantEntity
            {
                Id           = Guid.NewGuid(),
                BrandId      = brandId,
                ItemId       = cmd.ItemId,
                FabricTypeId = fid,
                Code         = $"{item.Code}-{meta?.Code ?? fid.ToString("N")[..6]}",
                VariantName  = meta?.Name ?? "Variant",
                DisplayOrder = 0,
                Status       = "active",
                CreatedAt    = now,
                UpdatedAt    = now,
                CreatedBy    = cmd.ActorId,
                UpdatedBy    = cmd.ActorId,
            });
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static object SnapshotPli(PliEntity e) => new
    {
        e.BasePrice, e.ExpressPrice, e.MinimumQuantity, e.TaxRatePercent,
        e.IsTaxable, e.DisplayLabel, e.IsActive,
    };
}
