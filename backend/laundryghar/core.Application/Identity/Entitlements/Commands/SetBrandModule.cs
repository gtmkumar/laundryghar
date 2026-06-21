using core.Application.Common.Interfaces;
using core.Application.Identity.Entitlements.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Entitlements.Commands;

public sealed record SetBrandModuleCommand(Guid BrandId, SetBrandModuleRequest Request, Guid? ActorId) : ICommand<bool>;

/// <summary>Toggle one module's licensing for a brand as a 'manual' override (upsert).
/// Manual rows take precedence over and survive bundle application.</summary>
public class SetBrandModuleCommandHandler : ICommandHandler<SetBrandModuleCommand, bool>
{
    private readonly ICoreDbContext _db;
    public SetBrandModuleCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(SetBrandModuleCommand cmd, CancellationToken ct)
    {
        var key = cmd.Request.ModuleKey;
        if (!await _db.Modules.AnyAsync(m => m.Key == key && m.Status == "active", ct))
            throw new ValidationException(new Dictionary<string, string[]> { ["moduleKey"] = ["Unknown module."] });

        var now = DateTimeOffset.UtcNow;
        var row = await _db.BrandModules
            .FirstOrDefaultAsync(bm => bm.BrandId == cmd.BrandId && bm.ModuleKey == key, ct);

        if (row is null)
        {
            _db.BrandModules.Add(new BrandModule
            {
                BrandId = cmd.BrandId, ModuleKey = key,
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
