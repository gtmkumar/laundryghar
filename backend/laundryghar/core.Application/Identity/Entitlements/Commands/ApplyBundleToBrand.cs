using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Commands;

public sealed record ApplyBundleToBrandCommand(Guid BrandId, ApplyBundleRequest Request, Guid? ActorId) : ICommand<bool>;

/// <summary>Apply a plan bundle to a brand: refresh the brand's 'bundle'-sourced rows to
/// exactly the bundle's modules. 'manual' overrides are preserved and take precedence
/// (so a manual disable survives, a manual enable stays).</summary>
public class ApplyBundleToBrandCommandHandler : ICommandHandler<ApplyBundleToBrandCommand, bool>
{
    private readonly ICoreDbContext _db;
    public ApplyBundleToBrandCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(ApplyBundleToBrandCommand cmd, CancellationToken ct)
    {
        var code = cmd.Request.BundleCode;
        var bundle = await _db.ModuleBundles.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code, ct);
        if (bundle is null)
            throw new ValidationException(new Dictionary<string, string[]> { ["bundleCode"] = ["Unknown bundle."] });

        var brandVertical = await _db.Brands.AsNoTracking()
            .Where(b => b.Id == cmd.BrandId).Select(b => b.VerticalKey).FirstOrDefaultAsync(ct);

        // A vertical-specific bundle can only be applied to a brand of that vertical.
        if (!VerticalKey.IsAvailableTo(bundle.VerticalKey, brandVertical))
            throw new ValidationException(new Dictionary<string, string[]>
                { ["bundleCode"] = ["This bundle is not available for the brand's vertical."] });

        // Expand the bundle, skipping modules not available to the brand's vertical — so a shared
        // tier bundle never licenses a laundry-only module (e.g. fabrics) to a salon brand.
        var bundleKeys = (await _db.ModuleBundleItems.AsNoTracking()
            .Where(i => i.BundleCode == code)
            .Join(_db.Modules.AsNoTracking(), i => i.ModuleKey, m => m.Key,
                  (i, m) => new { m.Key, m.VerticalKey })
            .ToListAsync(ct))
            .Where(m => VerticalKey.IsAvailableTo(m.VerticalKey, brandVertical))
            .Select(m => m.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await _db.BrandModules
            .Where(bm => bm.BrandId == cmd.BrandId)
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(r => r.ModuleKey, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        // Remove bundle-sourced rows that are NOT in the new bundle (manual rows untouched).
        foreach (var row in existing.Where(r => r.Source == "bundle" && !bundleKeys.Contains(r.ModuleKey)))
            _db.BrandModules.Remove(row);

        // Ensure every bundle module is licensed. Manual rows win and are left as-is;
        // bundle rows are (re)enabled; missing rows are inserted as 'bundle'.
        foreach (var key in bundleKeys)
        {
            if (byKey.TryGetValue(key, out var row))
            {
                if (row.Source == "manual") continue; // manual override preserved
                row.Enabled = true;
                row.ValidUntil = null;
                row.UpdatedAt = now;
                row.UpdatedBy = cmd.ActorId;
            }
            else
            {
                _db.BrandModules.Add(new BrandModule
                {
                    BrandId = cmd.BrandId, ModuleKey = key, Enabled = true, Source = "bundle",
                    CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
                });
            }
        }

        // Record the brand's platform subscription from this priced tier and issue the first
        // invoice for the current period. Unpriced/custom bundles create no billable subscription.
        if (bundle.Price is decimal price)
        {
            var interval = bundle.BillingInterval ?? "monthly";
            var currency = bundle.CurrencyCode ?? "INR";
            var sub = await _db.BrandPlatformSubscriptions.FirstOrDefaultAsync(s => s.BrandId == cmd.BrandId, ct);
            if (sub is null)
            {
                sub = new BrandPlatformSubscription
                {
                    Id = Guid.NewGuid(), BrandId = cmd.BrandId,
                    BundleCode = bundle.Code, PlanName = bundle.Name,
                    Price = price, BillingInterval = interval, CurrencyCode = currency, Status = "active",
                    CurrentPeriodStart = now, CurrentPeriodEnd = AddInterval(now, interval),
                    NextBillingAt = AddInterval(now, interval), AutoRenew = true,
                    CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
                };
                _db.BrandPlatformSubscriptions.Add(sub);
            }
            else
            {
                // Tier change: refresh the plan + price snapshot; keep the running period (proration TBD).
                sub.BundleCode = bundle.Code; sub.PlanName = bundle.Name;
                sub.Price = price; sub.BillingInterval = interval; sub.CurrencyCode = currency;
                sub.Status = "active"; sub.UpdatedAt = now; sub.UpdatedBy = cmd.ActorId;
            }

            var hasInvoice = await _db.BrandPlatformInvoices
                .AnyAsync(i => i.SubscriptionId == sub.Id && i.BillingPeriodStart == sub.CurrentPeriodStart, ct);
            if (!hasInvoice)
            {
                _db.BrandPlatformInvoices.Add(new BrandPlatformInvoice
                {
                    Id = Guid.NewGuid(), SubscriptionId = sub.Id, BrandId = cmd.BrandId,
                    BillingPeriodStart = sub.CurrentPeriodStart, BillingPeriodEnd = sub.CurrentPeriodEnd,
                    Amount = sub.Price, CurrencyCode = sub.CurrencyCode, Status = "issued",
                    IssuedAt = now, DueAt = now.AddDays(7), CreatedAt = now,
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        // Invalidate brand-scoped members' tokens so the plan change applies live.
        await Common.PermVersionBumper.BumpBrandMembersAsync(_db, cmd.BrandId, ct);
        return true;
    }

    /// <summary>Advance a timestamp by one billing interval.</summary>
    internal static DateTimeOffset AddInterval(DateTimeOffset from, string interval) => interval switch
    {
        "quarterly"   => from.AddMonths(3),
        "half_yearly" => from.AddMonths(6),
        "yearly"      => from.AddMonths(12),
        _             => from.AddMonths(1), // monthly (default)
    };
}
