using laundryghar.Identity.Application.AccessControl.Dtos;
using laundryghar.Identity.Application.Users.Commands;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Identity.Application.AccessControl.Commands;

// ── Invite user (create + grant primary membership) ─────────────────────────
public sealed record InviteUserCommand(InviteUserRequest Request, ICurrentUser Actor) : IRequest<UserDto>;

public sealed class InviteUserHandler : IRequestHandler<InviteUserCommand, UserDto>
{
    private readonly ISender _sender;
    public InviteUserHandler(ISender sender) => _sender = sender;

    public async Task<UserDto> Handle(InviteUserCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        var user = await _sender.Send(new CreateUserCommand(
            new CreateUserRequest(r.Email, r.Phone, r.UserType, r.Password, r.FirstName, r.LastName, null),
            cmd.Actor.UserId), ct);

        await _sender.Send(new GrantMembershipCommand(
            new GrantMembershipRequest(user.Id, r.ScopeType, r.ScopeId, r.RoleId, IsPrimary: true),
            cmd.Actor.UserId, cmd.Actor), ct);

        return user;
    }
}

// ── Toggle a permission-matrix cell for a role ──────────────────────────────
public sealed record SetRoleCellCommand(SetRoleCellRequest Request, Guid? ActorId) : IRequest<bool>;

public sealed class SetRoleCellHandler : IRequestHandler<SetRoleCellCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    public SetRoleCellHandler(LaundryGharDbContext db) => _db = db;

    public async Task<bool> Handle(SetRoleCellCommand cmd, CancellationToken ct)
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
        return true;
    }
}
