using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
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
        if (!await _db.ModuleBundles.AnyAsync(b => b.Code == code, ct))
            throw new ValidationException(new Dictionary<string, string[]> { ["bundleCode"] = ["Unknown bundle."] });

        var bundleKeys = (await _db.ModuleBundleItems.AsNoTracking()
            .Where(i => i.BundleCode == code)
            .Select(i => i.ModuleKey)
            .ToListAsync(ct))
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

        await _db.SaveChangesAsync(ct);

        // Invalidate brand-scoped members' tokens so the plan change applies live.
        await Common.PermVersionBumper.BumpBrandMembersAsync(_db, cmd.BrandId, ct);
        return true;
    }
}
