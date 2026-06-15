using core.Application.Common.Interfaces;
using core.Application.Identity.Users.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.AccessControl.Commands.RevokeMembership;

public sealed record RevokeMembershipCommand(RevokeMembershipRequest Request, Guid? ActorId) : ICommand<bool>;

public class RevokeMembershipCommandHandler : ICommandHandler<RevokeMembershipCommand, bool>
{
    private readonly ICoreDbContext _db;
    public RevokeMembershipCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(RevokeMembershipCommand cmd, CancellationToken ct)
    {
        var m = await _db.UserScopeMemberships.FindAsync([cmd.Request.MembershipId], ct);
        if (m is null || m.RevokedAt.HasValue) return false;
        m.RevokedAt = DateTimeOffset.UtcNow; m.RevokedBy = cmd.ActorId; m.RevokedReason = cmd.Request.Reason;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
