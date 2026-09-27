using core.Application.Common.Interfaces;
using core.Application.Identity.Common;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.AssignPermission;

public sealed record AssignPermissionCommand(AssignPermissionRequest Request, Guid? ActorId) : ICommand<bool>;

/// <summary>
/// Grants one permission to one role.
///
/// A0.4 — this handler was eleven lines with no guards at all, sitting behind
/// <c>permissions.assign</c> (risk_level = critical) and reachable from the single "Users · Approve"
/// checkbox on the access-control matrix. It did not check that the role existed, that the
/// permission existed, that the role belonged to the caller's brand, or that the caller outranked
/// the role being edited — so any holder of one critical permission could grant themselves every
/// other one. It also never bumped <c>perm_version</c>, so a grant did not reach live tokens until
/// their natural refresh, while its sibling <see cref="SetRoleCells"/> did.
///
/// The authority guards moved to <see cref="RoleEditGuard"/> in the A-1 fix, so this handler and
/// <c>SetRoleCells</c> — the two write paths into role_permissions — enforce one rule rather than
/// two that drifted apart.
/// </summary>
public class AssignPermissionCommandHandler : ICommandHandler<AssignPermissionCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _actor;

    public AssignPermissionCommandHandler(ICoreDbContext db, ICurrentUser actor)
    {
        _db = db;
        _actor = actor;
    }

    public async Task<bool> HandleAsync(AssignPermissionCommand cmd, CancellationToken ct)
    {
        // ── The role must exist and be live ───────────────────────────────────────────────
        var role = await RoleEditGuard.FindLiveRoleAsync(_db, cmd.Request.RoleId, ct);
        if (role is null)
            throw new ValidationException(new Dictionary<string, string[]>
                { ["roleId"] = ["Role not found."] });

        // ── The permission must exist ─────────────────────────────────────────────────────
        var permission = await _db.Permissions.AsNoTracking()
            .Where(p => p.Id == cmd.Request.PermissionId)
            .Select(p => new { p.Id, p.Code })
            .FirstOrDefaultAsync(ct);
        if (permission is null)
            throw new ValidationException(new Dictionary<string, string[]>
                { ["permissionId"] = ["Permission not found."] });

        // ── System-role, brand-isolation and rank guards ──────────────────────────────────
        // Shared with SetRoleCells; see RoleEditGuard for why these no longer live inline.
        await RoleEditGuard.EnsureMayEditAsync(_db, _actor, cmd.ActorId, role, ct);

        var now = DateTimeOffset.UtcNow;

        var existing = await _db.RolePermissions
            .FirstOrDefaultAsync(rp => rp.RoleId == cmd.Request.RoleId
                                    && rp.PermissionId == cmd.Request.PermissionId, ct);

        if (existing is not null)
        {
            // Effect-aware, same rule as SetRoleCells (A0.3): an existing ALLOW is idempotent, but a
            // DENY must be flipped rather than reported as already-granted. The old code returned
            // true on any existing row, so assigning a denied permission silently did nothing.
            if (existing.Effect != "deny") return true;

            existing.Effect    = "allow";
            existing.GrantedAt = now;
            existing.GrantedBy = cmd.ActorId;
        }
        else
        {
            _db.RolePermissions.Add(new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = cmd.Request.RoleId,
                PermissionId = cmd.Request.PermissionId,
                Effect = "allow",
                GrantedAt = now, GrantedBy = cmd.ActorId,
                CreatedAt = now, CreatedBy = cmd.ActorId,
            });
        }

        await _db.SaveChangesAsync(ct);

        // Propagate to live tokens within the ~15s EnforceTokenVersion bound, as SetRoleCells does.
        await PermVersionBumper.BumpRoleHoldersAsync(_db, cmd.Request.RoleId, ct);
        return true;
    }
}
