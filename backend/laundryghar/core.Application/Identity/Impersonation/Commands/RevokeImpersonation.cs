using core.Application.Common.Interfaces;
using core.Application.Identity.Impersonation.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth.Audit;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Impersonation.Commands;

public sealed record RevokeImpersonationCommand(Guid GrantId, RevokeImpersonationRequest Request, Guid? ActorId)
    : ICommand<bool>;

/// <summary>
/// The provider ends a session. The single most important operation here: consent that cannot be
/// withdrawn is not consent.
///
/// <para>It takes effect on the very next request, not when a token expires, because
/// <c>ImpersonationGuardMiddleware</c> re-reads grant state every time rather than trusting the
/// claim. That is what makes this button honest.</para>
///
/// <para>Revoking a <c>pending</c> request is allowed and means "withdraw it" — an owner should not
/// have to approve something in order to be able to cancel it.</para>
/// </summary>
public sealed class RevokeImpersonationCommandHandler : ICommandHandler<RevokeImpersonationCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly IAuditWriter _audit;

    public RevokeImpersonationCommandHandler(ICoreDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<bool> HandleAsync(RevokeImpersonationCommand cmd, CancellationToken ct)
    {
        var grant = await _db.ImpersonationGrants.FirstOrDefaultAsync(g => g.Id == cmd.GrantId, ct);
        if (grant is null) return false;

        // Already finished — report success rather than error. The user's intent ("this must not be
        // usable") is satisfied, and a red banner on a session that is already dead is just noise.
        if (grant.Status is ImpersonationGrantStatus.Revoked
                         or ImpersonationGrantStatus.Denied
                         or ImpersonationGrantStatus.Expired)
            return true;

        var now = DateTimeOffset.UtcNow;
        grant.Status = ImpersonationGrantStatus.Revoked;
        grant.RevokedAt = now;
        grant.RevokedByUserId = cmd.ActorId;
        grant.RevokeReason = cmd.Request.Reason;
        grant.UpdatedAt = now;
        grant.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("impersonation.revoked", "impersonation_grant", grant.Id,
            grant.Reason, newValues: new { revokedBy = cmd.ActorId, reason = cmd.Request.Reason }, ct: ct);

        return true;
    }
}
