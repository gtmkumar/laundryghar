using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.Common;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl.Commands.SetRoleCells;

public sealed record SetRoleCellsCommand(Guid RoleId, SetRoleCellsRequest Request, Guid? ActorId) : ICommand<bool>;

/// <summary>
/// Applies a batch of matrix-cell changes to one role in a SINGLE transaction (one SaveChanges),
/// so a save is all-or-nothing — no partial-write window if one cell fails. Mirrors SetRoleCell's
/// cell→permission mapping and self-lockout guard, but for the whole diff at once.
///
/// <para><b>A-1 (audit 2026-09-05) — guard asymmetry.</b> This handler previously ran with only the
/// self-lockout check below. Its sibling <c>AssignPermission</c>, behind the same
/// <c>permissions.assign</c> policy and writing the same table, additionally refused to edit a
/// system role, a role outside the caller's brand, or a role outranking the caller. So the harder
/// route was guarded and the easier one — one matrix cell, flipping many codes at once — was not:
/// a brand admin could add permissions to <c>platform_admin</c> itself. The three guards now come
/// from <see cref="RoleEditGuard"/>, which both handlers call.</para>
/// </summary>
public class SetRoleCellsCommandHandler : ICommandHandler<SetRoleCellsCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;
    public SetRoleCellsCommandHandler(ICoreDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(SetRoleCellsCommand cmd, CancellationToken ct)
    {
        var roleId = cmd.RoleId;
        var role = await RoleEditGuard.FindLiveRoleAsync(_db, roleId, ct);
        if (role is null) return false;                       // 404, as before

        // Authority to edit THIS role at all — checked before any cell is resolved, so an
        // unauthorized save costs one query and changes nothing.
        await RoleEditGuard.EnsureMayEditAsync(_db, _user, cmd.ActorId, role, ct);

        var changes = cmd.Request.Changes;
        if (changes.Count == 0) return true;

        var matrix = await ModuleMatrix.LoadAsync(_db, ct);
        var perms = await _db.Permissions.AsNoTracking().Select(p => new { p.Id, p.Code }).ToListAsync(ct);

        // cellKey → permission ids it maps to.
        var byCell = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in perms)
            foreach (var cell in matrix.CellsFor(PermissionMatrix.Module(p.Code), PermissionMatrix.Action(p.Code)))
                (byCell.TryGetValue(cell, out var s) ? s : byCell[cell] = []).Add(p.Id);

        // Resolve the batch into permission ids to add / remove (a code enabled by any cell wins over a remove).
        var toEnable = new HashSet<Guid>();
        var toDisable = new HashSet<Guid>();
        foreach (var ch in changes)
        {
            if (!byCell.TryGetValue(ch.CellKey, out var ids)) continue;
            if (ch.Enabled) toEnable.UnionWith(ids); else toDisable.UnionWith(ids);
        }
        toDisable.ExceptWith(toEnable);

        // Self-lockout guard: a non-platform admin can't strip their OWN role of role/permission management.
        if (toDisable.Count > 0 && !_user.IsPlatformAdmin && cmd.ActorId is { } actorId)
        {
            var mgmtIds = perms.Where(p => p.Code is "permissions.assign" or "roles.manage").Select(p => p.Id).ToHashSet();
            if (toDisable.Overlaps(mgmtIds)
                && await _db.UserScopeMemberships.AnyAsync(
                    m => m.RoleId == roleId && m.UserId == actorId && m.RevokedAt == null, ct))
                throw new ValidationException(new Dictionary<string, string[]>
                    { ["changes"] = ["You can't remove your own permission to manage roles — ask another admin."] });
        }

        var affected = toEnable.Concat(toDisable).ToHashSet();
        var existing = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId && affected.Contains(rp.PermissionId))
            .ToListAsync(ct);
        var have = existing.Select(rp => rp.PermissionId).ToHashSet();
        var now = DateTimeOffset.UtcNow;

        // A0.3 — effect-aware enable/disable. Previously `have` counted a DENY row as "already
        // granted", so ticking a denied cell inserted nothing, left the deny in place, and returned
        // success: the box was already ticked (see A0.2) and stayed ticked. Enabling must now FLIP an
        // existing deny to allow, and disabling must remove only the allow — a deny is a stronger
        // statement than absence and is not what an unticked box asks for.
        foreach (var rp in existing.Where(rp => toEnable.Contains(rp.PermissionId) && rp.Effect == "deny"))
        {
            rp.Effect    = "allow";
            rp.GrantedAt = now;
            rp.GrantedBy = cmd.ActorId;
        }

        foreach (var pid in toEnable.Where(id => !have.Contains(id)))
            _db.RolePermissions.Add(new RolePermission
            {
                Id = Guid.NewGuid(), RoleId = roleId, PermissionId = pid,
                GrantedAt = now, GrantedBy = cmd.ActorId, CreatedAt = now, CreatedBy = cmd.ActorId,
            });
        _db.RolePermissions.RemoveRange(
            existing.Where(rp => toDisable.Contains(rp.PermissionId) && rp.Effect != "deny"));

        await _db.SaveChangesAsync(ct); // single transaction — atomic

        await PermVersionBumper.BumpRoleHoldersAsync(_db, roleId, ct);
        return true;
    }
}
