using core.Application.Common.Interfaces;
using core.Application.Identity.Common;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.SetUserPermissionOverride;

/// <summary>Set or clear a per-user permission override. Effect "allow"/"deny" upserts it;
/// null/empty removes it (reverting to role-derived behaviour).</summary>
public sealed record SetUserPermissionOverrideRequest(string PermissionCode, string? Effect);

public sealed record SetUserPermissionOverrideCommand(Guid UserId, SetUserPermissionOverrideRequest Request, Guid? ActorId)
    : ICommand<bool>;

public class SetUserPermissionOverrideCommandHandler : ICommandHandler<SetUserPermissionOverrideCommand, bool>
{
    private readonly ICoreDbContext _db;
    public SetUserPermissionOverrideCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(SetUserPermissionOverrideCommand cmd, CancellationToken ct)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == cmd.UserId && u.DeletedAt == null, ct)) return false;

        var effect = cmd.Request.Effect?.Trim().ToLowerInvariant();
        if (effect is not (null or "" or "allow" or "deny"))
            throw new ValidationException(new Dictionary<string, string[]> { ["effect"] = ["Must be 'allow', 'deny', or null."] });

        var perm = await _db.Permissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == cmd.Request.PermissionCode, ct)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["permissionCode"] = ["Unknown permission."] });

        var existing = await _db.UserPermissionOverrides
            .FirstOrDefaultAsync(o => o.UserId == cmd.UserId && o.PermissionId == perm.Id, ct);

        if (string.IsNullOrEmpty(effect))
        {
            if (existing is not null) _db.UserPermissionOverrides.Remove(existing);
        }
        else if (existing is null)
        {
            var now = DateTimeOffset.UtcNow;
            _db.UserPermissionOverrides.Add(new UserPermissionOverride
            {
                UserId = cmd.UserId, PermissionId = perm.Id, Effect = effect,
                GrantedAt = now, GrantedBy = cmd.ActorId, CreatedAt = now,
            });
        }
        else
        {
            existing.Effect = effect;
            existing.GrantedBy = cmd.ActorId;
        }

        await _db.SaveChangesAsync(ct);

        // Live revocation: invalidate the user's existing tokens.
        await PermVersionBumper.BumpUserAsync(_db, cmd.UserId, ct);
        return true;
    }
}
