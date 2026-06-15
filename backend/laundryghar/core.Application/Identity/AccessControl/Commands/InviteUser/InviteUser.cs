using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Commands.GrantMembership;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.Settings;
using core.Application.Identity.Users.Commands.CreateUser;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace core.Application.Identity.AccessControl.Commands.InviteUser;

// ── Invite user (create + grant primary membership) ─────────────────────────
public sealed record InviteUserCommand(InviteUserRequest Request) : ICommand<UserDto>;

public class InviteUserCommandHandler : ICommandHandler<InviteUserCommand, UserDto>
{
    private readonly IDispatcher _dispatcher;
    private readonly ICoreDbContext _db;
    private readonly ISettingsMailer _mailer;
    private readonly ICurrentUser _actor;
    private readonly ILogger<InviteUserCommandHandler> _log;

    public InviteUserCommandHandler(IDispatcher dispatcher, ICoreDbContext db, ISettingsMailer mailer, ICurrentUser actor, ILogger<InviteUserCommandHandler> log)
    { _dispatcher = dispatcher; _db = db; _mailer = mailer; _actor = actor; _log = log; }

    public async Task<UserDto> HandleAsync(InviteUserCommand cmd, CancellationToken ct)
    {
        var r = cmd.Request;
        var user = await _dispatcher.SendAsync(new CreateUserCommand(
            new CreateUserRequest(r.Email, r.Phone, r.UserType, r.Password, r.FirstName, r.LastName, null),
            _actor.UserId), ct);

        await _dispatcher.SendAsync(new GrantMembershipCommand(
            new GrantMembershipRequest(user.Id, r.ScopeType, r.ScopeId, r.RoleId, IsPrimary: true),
            _actor.UserId), ct);

        await SendInviteEmailAsync(_actor, user.Id, r.Email, $"{r.FirstName} {r.LastName}".Trim(), ct);
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
