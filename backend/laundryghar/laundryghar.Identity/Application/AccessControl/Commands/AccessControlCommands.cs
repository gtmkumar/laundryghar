using laundryghar.Identity.Application.AccessControl.Dtos;
using laundryghar.Identity.Application.Settings;
using laundryghar.Identity.Application.Users.Commands;
using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.Identity.Infrastructure.Email;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace laundryghar.Identity.Application.AccessControl.Commands;

// ── Invite user (create + grant primary membership) ─────────────────────────
public sealed record InviteUserCommand(InviteUserRequest Request, ICurrentUser Actor) : IRequest<UserDto>;

public sealed class InviteUserHandler : IRequestHandler<InviteUserCommand, UserDto>
{
    private readonly ISender _sender;
    private readonly LaundryGharDbContext _db;
    private readonly ISettingsMailer _mailer;
    private readonly ILogger<InviteUserHandler> _log;

    public InviteUserHandler(ISender sender, LaundryGharDbContext db, ISettingsMailer mailer, ILogger<InviteUserHandler> log)
    { _sender = sender; _db = db; _mailer = mailer; _log = log; }

    public async Task<UserDto> Handle(InviteUserCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        var user = await _sender.Send(new CreateUserCommand(
            new CreateUserRequest(r.Email, r.Phone, r.UserType, r.Password, r.FirstName, r.LastName, null),
            cmd.Actor.UserId), ct);

        await _sender.Send(new GrantMembershipCommand(
            new GrantMembershipRequest(user.Id, r.ScopeType, r.ScopeId, r.RoleId, IsPrimary: true),
            cmd.Actor.UserId, cmd.Actor), ct);

        await SendInviteEmailAsync(cmd.Actor, user.Id, r.Email, $"{r.FirstName} {r.LastName}".Trim(), ct);
        return user;
    }

    // Best-effort: an email failure must never roll back a successful invite.
    private async Task SendInviteEmailAsync(ICurrentUser actor, Guid userId, string? email, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        try
        {
            var mode = await SettingsStore.LoadProvisioningModeAsync(_db, actor.BrandId, ct);
            if (mode == "self_service")
            {
                var token = await _db.Users.AsNoTracking().Where(u => u.Id == userId)
                    .Select(u => u.InvitationToken).FirstOrDefaultAsync(ct);
                if (string.IsNullOrEmpty(token))
                {
                    _log.LogWarning("Invited user {UserId} has no invitation token; skipping self-service email.", userId);
                    return;
                }
                var baseUrl = (await SettingsStore.LoadAdminBaseUrlAsync(_db, actor.BrandId, ct)).TrimEnd('/');
                var acceptUrl = $"{baseUrl}/accept-invite?token={Uri.EscapeDataString(token)}";
                var (subject, html) = EmailTemplates.InviteSelfService(name, acceptUrl);
                await _mailer.SendAsync(actor.BrandId, email, subject, html, ct);
            }
            else
            {
                var (subject, html) = EmailTemplates.InviteAdminActivate(name);
                await _mailer.SendAsync(actor.BrandId, email, subject, html, ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send invite email to {Email}.", email);
        }
    }
}

// ── Invite a rider (franchise-scoped, requires rider.manage) ────────────────
public sealed record InviteRiderCommand(InviteRiderRequest Request, ICurrentUser Actor) : IRequest<UserDto>;

public sealed class InviteRiderHandler : IRequestHandler<InviteRiderCommand, UserDto>
{
    private readonly ISender _sender;
    private readonly LaundryGharDbContext _db;

    public InviteRiderHandler(ISender sender, LaundryGharDbContext db)
    { _sender = sender; _db = db; }

    public async Task<UserDto> Handle(InviteRiderCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;

        if (string.IsNullOrWhiteSpace(r.Email))
            throw new ValidationException(
                new Dictionary<string, string[]> { ["email"] = ["Email is required."] });

        // ── Resolve the seeded rider role ────────────────────────────────────
        var riderRoleId = await _db.Roles
            .Where(ro => ro.Code == "rider" && ro.DeletedAt == null)
            .Select(ro => (Guid?)ro.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new ValidationException(
                new Dictionary<string, string[]> { ["role"] = ["The seeded 'rider' role was not found. Contact a platform administrator."] });

        // ── Franchise scoping ────────────────────────────────────────────────
        // Franchise-scoped actors are locked to their own franchise; brand/platform
        // admins supply the franchiseId in the request but we validate brand ownership.
        Guid franchiseId;

        if (cmd.Actor.FranchiseId is Guid actorFid)
        {
            // Franchise owner / franchise staff: force their own franchise regardless of request.
            franchiseId = actorFid;
        }
        else
        {
            // Brand admin or platform admin: use the request value, validate it belongs to their brand.
            if (r.FranchiseId == Guid.Empty)
                throw new ValidationException(
                    new Dictionary<string, string[]> { ["franchiseId"] = ["FranchiseId is required."] });

            franchiseId = r.FranchiseId;

            if (!cmd.Actor.IsPlatformAdmin)
            {
                var brandId = cmd.Actor.BrandId
                    ?? throw new ValidationException(
                        new Dictionary<string, string[]> { ["actor"] = ["Could not determine your brand context. Re-authenticate and try again."] });

                var franchiseBelongsToBrand = await _db.Franchises
                    .AnyAsync(f => f.Id == franchiseId && f.BrandId == brandId && f.DeletedAt == null, ct);

                if (!franchiseBelongsToBrand)
                    throw new ValidationException(
                        new Dictionary<string, string[]> { ["franchiseId"] = ["Franchise not found or does not belong to your brand."] });
            }
        }

        // ── Delegate to the shared InviteUserCommand (creates user + membership + email) ──
        return await _sender.Send(new InviteUserCommand(
            new InviteUserRequest(
                Email:     r.Email,
                Phone:     r.Phone,
                FirstName: r.FirstName,
                LastName:  r.LastName,
                UserType:  "rider",
                RoleId:    riderRoleId,
                ScopeType: "franchise",
                ScopeId:   franchiseId,
                Password:  null),
            cmd.Actor), ct);
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

// ── Activate / suspend / reactivate a person ────────────────────────────────
public sealed record SetPersonStatusCommand(Guid UserId, SetPersonStatusRequest Request, Guid? ActorId)
    : IRequest<SetPersonStatusResult?>;

public sealed class SetPersonStatusHandler : IRequestHandler<SetPersonStatusCommand, SetPersonStatusResult?>
{
    private readonly LaundryGharDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ISettingsMailer _mailer;
    private readonly ILogger<SetPersonStatusHandler> _log;
    public SetPersonStatusHandler(LaundryGharDbContext db, IPasswordHasher hasher, ISettingsMailer mailer, ILogger<SetPersonStatusHandler> log)
    { _db = db; _hasher = hasher; _mailer = mailer; _log = log; }

    public async Task<SetPersonStatusResult?> Handle(SetPersonStatusCommand cmd, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId && u.Status != UserStatus.Deleted, ct);
        if (user is null) return null;

        var now = DateTimeOffset.UtcNow;
        string? tempPasswordToEmail = null;
        switch (cmd.Request.Action?.Trim().ToLowerInvariant())
        {
            case "activate":
                // Invited (or locked) accounts have no usable password yet — set the
                // admin-provided temporary one and force a reset on first login.
                var pwd = cmd.Request.Password;
                if (string.IsNullOrWhiteSpace(pwd) || pwd.Length < 8)
                    throw new ValidationException(new Dictionary<string, string[]>
                        { ["password"] = ["A temporary password of at least 8 characters is required to activate this user."] });
                user.PasswordHash       = _hasher.Hash(pwd);
                user.PasswordChangedAt  = now;
                user.MustChangePassword = true;
                user.InvitationToken    = null;
                user.Status             = UserStatus.Active;
                tempPasswordToEmail     = pwd;
                break;

            case "suspend":
                user.Status = UserStatus.Suspended;
                break;

            case "reactivate":
                // Already has a password from a prior activation — just lift the suspension.
                if (user.PasswordHash is null)
                    throw new ValidationException(new Dictionary<string, string[]>
                        { ["action"] = ["This user has never been activated — use Activate to set a password."] });
                user.Status = UserStatus.Active;
                break;

            default:
                throw new ValidationException(new Dictionary<string, string[]>
                    { ["action"] = ["Action must be one of: activate, suspend, reactivate."] });
        }

        user.UpdatedAt = now;
        user.UpdatedBy = cmd.ActorId;
        user.Version++;
        await _db.SaveChangesAsync(ct);

        // Best-effort: email the freshly-activated user their temporary password.
        if (tempPasswordToEmail is not null && !string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                // Users carry no direct brand_id (brand comes via membership); resolve
                // the settings brand from the request's RLS scope (null → first brand).
                var baseUrl = (await SettingsStore.LoadAdminBaseUrlAsync(_db, null, ct)).TrimEnd('/');
                var name = await _db.UserProfiles.AsNoTracking().Where(p => p.UserId == user.Id)
                    .Select(p => ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim()).FirstOrDefaultAsync(ct);
                var (subject, html) = EmailTemplates.Activated(name ?? "", user.Email!, tempPasswordToEmail, $"{baseUrl}/login");
                await _mailer.SendAsync(null, user.Email!, subject, html, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to send activation email to {Email}.", user.Email);
            }
        }

        return new SetPersonStatusResult(user.Status, user.MustChangePassword);
    }
}
