using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.SetRoleCell;

public sealed record SetRoleCellCommand(SetRoleCellRequest Request, Guid? ActorId) : ICommand<bool>;

public class SetRoleCellCommandHandler : ICommandHandler<SetRoleCellCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public SetRoleCellCommandHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(SetRoleCellCommand cmd, CancellationToken ct)
    {
        var roleId = cmd.Request.RoleId;
        if (!await _db.Roles.AnyAsync(r => r.Id == roleId && r.DeletedAt == null, ct)) return false;

        // All permissions whose (data-driven) matrix mapping includes this cell.
        var matrix = await ModuleMatrix.LoadAsync(_db, ct);
        var perms = await _db.Permissions.AsNoTracking()
            .Select(p => new { p.Id, p.Code }).ToListAsync(ct);
        var targetIds = perms
            .Where(p => matrix.CellsFor(PermissionMatrix.Module(p.Code), PermissionMatrix.Action(p.Code))
                .Contains(cmd.Request.CellKey))
            .Select(p => p.Id)
            .ToHashSet();
        if (targetIds.Count == 0) return true; // nothing maps here — no-op

        // Self-lockout guard: a non-platform admin can't strip their OWN role of the ability
        // to manage roles/permissions — that would lock them out of this very screen.
        if (!cmd.Request.Enabled && !_user.IsPlatformAdmin && cmd.ActorId is { } actorId)
        {
            var mgmtIds = perms
                .Where(p => p.Code is "permissions.assign" or "roles.manage")
                .Select(p => p.Id).ToHashSet();
            if (targetIds.Overlaps(mgmtIds)
                && await _db.UserScopeMemberships.AnyAsync(
                    m => m.RoleId == roleId && m.UserId == actorId && m.RevokedAt == null, ct))
            {
                throw new ValidationException(new Dictionary<string, string[]>
                    { ["cellKey"] = ["You can't remove your own permission to manage roles — ask another admin."] });
            }
        }

        var existing = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId && targetIds.Contains(rp.PermissionId))
            .ToListAsync(ct);

        if (cmd.Request.Enabled)
        {
            var have = existing.Select(rp => rp.PermissionId).ToHashSet();
            var now = DateTimeOffset.UtcNow;
            foreach (var pid in targetIds.Where(id => !have.Contains(id)))
                _db.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(), RoleId = roleId, PermissionId = pid,
                    GrantedAt = now, GrantedBy = cmd.ActorId, CreatedAt = now, CreatedBy = cmd.ActorId,
                });
        }
        else
        {
            _db.RolePermissions.RemoveRange(existing);
        }

        await _db.SaveChangesAsync(ct);

        // Invalidate tokens of everyone holding this role (live revocation).
        await core.Application.Identity.Common.PermVersionBumper.BumpRoleHoldersAsync(_db, roleId, ct);
        return true;
    }
}
