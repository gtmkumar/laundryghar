using System.Text.Json;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Common.Interfaces;

namespace operations.Application.Catalog.Pricing.Commands.Revert;

public sealed record RevertPricingChangeCommand(Guid LogId, Guid? ActorId) : ICommand<bool>;

/// <summary>Restores the before-state captured in a pricing change-log entry, then stamps it reverted.
/// Supports fabric_type (multiplier + fields), price_list_item (rates + fields), value_price_slab, and
/// item (editable fields — UPDATE entries only; create/delete are not revertible).</summary>
public sealed class RevertPricingChangeHandler : ICommandHandler<RevertPricingChangeCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    public RevertPricingChangeHandler(IOperationsDbContext db, ICurrentUser user) { _db = db; _user = user; }

    private sealed record FabricSnap(string Name, string NameLocalized, string? Description, string? CareInstructions,
        decimal PriceMultiplier, bool RequiresSpecialCare, short DisplayOrder, string Status);
    private sealed record ItemSnap(decimal BasePrice, decimal? ExpressPrice, int MinimumQuantity,
        decimal TaxRatePercent, bool IsTaxable, string? DisplayLabel, string? Notes, bool IsActive);
    private sealed record SlabSnap(Guid? ServiceId, decimal MinValue, decimal? MaxValue, decimal Price, string? Status);

    public async Task<bool> HandleAsync(RevertPricingChangeCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var log = await _db.PricingChangeLogs
            .FirstOrDefaultAsync(x => x.Id == cmd.LogId && x.BrandId == brandId, ct);
        if (log is null) return false;
        if (log.RevertedAt != null)
            throw new BusinessRuleException("This change has already been reverted.");
        if (string.IsNullOrEmpty(log.BeforeJson))
            throw new BusinessRuleException("This entry has no prior state to restore.");

        switch (log.TargetKind)
        {
            case "fabric_type":
            {
                var s = JsonSerializer.Deserialize<FabricSnap>(log.BeforeJson)
                    ?? throw new BusinessRuleException("Corrupt snapshot.");
                var e = await _db.FabricTypes.FirstOrDefaultAsync(x => x.Id == log.TargetId && x.BrandId == brandId, ct);
                if (e is null || e.DeletedAt != null) throw new BusinessRuleException("Fabric type no longer exists.");
                e.Name = s.Name; e.NameLocalized = s.NameLocalized; e.Description = s.Description;
                e.CareInstructions = s.CareInstructions; e.PriceMultiplier = s.PriceMultiplier;
                e.RequiresSpecialCare = s.RequiresSpecialCare; e.DisplayOrder = s.DisplayOrder; e.Status = s.Status;
                e.UpdatedAt = DateTimeOffset.UtcNow; e.UpdatedBy = cmd.ActorId;
                break;
            }
            case "price_list_item":
            {
                var s = JsonSerializer.Deserialize<ItemSnap>(log.BeforeJson)
                    ?? throw new BusinessRuleException("Corrupt snapshot.");
                var e = await _db.PriceListItems.FirstOrDefaultAsync(x => x.Id == log.TargetId && x.BrandId == brandId, ct);
                if (e is null) throw new BusinessRuleException("Price row no longer exists.");
                e.BasePrice = s.BasePrice; e.ExpressPrice = s.ExpressPrice; e.MinimumQuantity = s.MinimumQuantity;
                e.TaxRatePercent = s.TaxRatePercent; e.IsTaxable = s.IsTaxable; e.DisplayLabel = s.DisplayLabel;
                e.Notes = s.Notes; e.IsActive = s.IsActive;
                e.UpdatedAt = DateTimeOffset.UtcNow; e.UpdatedBy = cmd.ActorId;
                break;
            }
            case "value_price_slab":
            {
                var s = JsonSerializer.Deserialize<SlabSnap>(log.BeforeJson)
                    ?? throw new BusinessRuleException("Corrupt snapshot.");
                var e = await _db.ValuePriceSlabs.FirstOrDefaultAsync(x => x.Id == log.TargetId && x.BrandId == brandId, ct);
                if (e is null) throw new BusinessRuleException("Value slab no longer exists.");
                // A create logs an empty before-state ({}) → snapshot Status is null; reverting a
                // create means archiving the slab. Otherwise restore the captured prior fields.
                if (string.IsNullOrEmpty(s.Status))
                {
                    e.Status = "archived";
                }
                else
                {
                    e.ServiceId = s.ServiceId; e.MinValue = s.MinValue; e.MaxValue = s.MaxValue;
                    e.Price = s.Price; e.Status = s.Status;
                }
                e.UpdatedAt = DateTimeOffset.UtcNow; e.UpdatedBy = cmd.ActorId; e.Version++;
                break;
            }
            case "item":
            {
                var env = JsonSerializer.Deserialize<ItemAuditEnvelope>(log.BeforeJson)
                    ?? throw new BusinessRuleException("Corrupt snapshot.");
                // Only UPDATE entries are revertible: they carry the prior editable-field state.
                // A create/delete has no meaningful "restore the previous fields" action.
                if (env.Op != ItemAudit.OpUpdate || env.State is null)
                    throw new BusinessRuleException(
                        "Only item edits can be reverted — item create and delete entries are not revertible.");

                var e = await _db.Items.FirstOrDefaultAsync(x => x.Id == log.TargetId && x.BrandId == brandId, ct);
                if (e is null || e.DeletedAt != null) throw new BusinessRuleException("Item no longer exists.");

                var s = env.State;
                // Restoring the code could collide with a code taken since the edit — guard it.
                if (!string.Equals(s.Code, e.Code, StringComparison.Ordinal))
                {
                    var taken = await _db.Items.AnyAsync(
                        x => x.BrandId == brandId && x.Id != e.Id && x.DeletedAt == null && x.Code == s.Code, ct);
                    if (taken)
                        throw new BusinessRuleException(
                            $"Cannot revert: the code “{s.Code}” is now used by another item.");
                }

                e.Name = s.Name; e.Code = s.Code; e.ItemGroupId = s.ItemGroupId; e.Status = s.Status;
                e.TatHours = s.TatHours; e.ExpressEligible = s.ExpressEligible;
                e.ExpressSurcharge = s.ExpressSurcharge; e.PricingMode = s.PricingMode;
                e.UpdatedAt = DateTimeOffset.UtcNow; e.UpdatedBy = cmd.ActorId; e.Version++;
                break;
            }
            default:
                throw new BusinessRuleException($"Revert not supported for '{log.TargetKind}'.");
        }

        log.RevertedAt = DateTimeOffset.UtcNow;
        log.RevertedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
