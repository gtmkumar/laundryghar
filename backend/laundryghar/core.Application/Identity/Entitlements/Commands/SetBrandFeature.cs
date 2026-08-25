using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Commands;

public sealed record SetBrandFeatureCommand(Guid BrandId, SetBrandFeatureRequest Request, Guid? ActorId) : ICommand<bool>;

/// <summary>Toggle one FEATURE's licensing for a brand as a 'manual' override (upsert) — the
/// à-la-carte add-on path of PLATFORM_STRATEGY.md §5. Manual rows take precedence over, and survive,
/// bundle application. Feature-keyed since migration 0005 (was SetBrandModule).</summary>
public class SetBrandFeatureCommandHandler : ICommandHandler<SetBrandFeatureCommand, bool>
{
    private readonly ICoreDbContext _db;
    public SetBrandFeatureCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(SetBrandFeatureCommand cmd, CancellationToken ct)
    {
        var key = cmd.Request.FeatureKey;
        if (!await _db.Features.AnyAsync(f => f.Key == key && f.Status == "active", ct))
            throw new ValidationException(new Dictionary<string, string[]> { ["featureKey"] = ["Unknown feature."] });

        var now = DateTimeOffset.UtcNow;
        var row = await _db.BrandFeatures
            .FirstOrDefaultAsync(bf => bf.BrandId == cmd.BrandId && bf.FeatureKey == key, ct);

        if (row is null)
        {
            _db.BrandFeatures.Add(new BrandFeature
            {
                BrandId = cmd.BrandId, FeatureKey = key,
                Enabled = cmd.Request.Enabled, ValidUntil = cmd.Request.ValidUntil,
                Source = "manual",
                CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
            });
        }
        else
        {
            row.Enabled = cmd.Request.Enabled;
            row.ValidUntil = cmd.Request.ValidUntil;
            row.Source = "manual";
            row.UpdatedAt = now;
            row.UpdatedBy = cmd.ActorId;
        }

        await _db.SaveChangesAsync(ct);

        // Invalidate brand-scoped members' tokens so the entitlement change applies live.
        await Common.PermVersionBumper.BumpBrandMembersAsync(_db, cmd.BrandId, ct);
        return true;
    }
}
