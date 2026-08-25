using core.Application.Common.Interfaces;
using core.Application.Identity.Impersonation.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.Impersonation.Commands;

public sealed record DecideImpersonationCommand(Guid GrantId, DecideImpersonationRequest Request, Guid? ActorId)
    : ICommand<bool>;

/// <summary>
/// The provider's answer to a request — the moment consent does or does not exist (§8.1 "recorded
/// consent"). Gated on <c>impersonation.approve</c>, a CRITICAL permission held by owner-side roles
/// only, so it also demands a fresh step-up (§8).
///
/// <para>Two rules the handler enforces that the table cannot:</para>
/// <list type="number">
/// <item><b>Never grant more than was asked for.</b> A request for read-only cannot be approved into
/// read-write. Widening scope at approval time would mean the thing the owner clicked "allow" on is
/// not the thing they were shown.</item>
/// <item><b>Only a pending request can be decided.</b> Re-deciding an approved grant would silently
/// extend a live session; ending one is <c>revoke</c>, and starting a new one is a new request.</item>
/// </list>
/// </summary>
public sealed class DecideImpersonationCommandHandler : ICommandHandler<DecideImpersonationCommand, bool>
{
    /// <summary>Default session length when the owner does not choose one. Short on purpose: a
    /// support call is an hour, not a day, and extending is one more click.</summary>
    public const int DefaultTtlMinutes = 60;

    /// <summary>Ceiling, mirroring the CHECK constraint in migration 0014. Duplicated deliberately
    /// so the caller gets a validation message instead of a constraint violation.</summary>
    public const int MaxTtlMinutes = 24 * 60;

    private readonly ICoreDbContext _db;
    private readonly IAuditWriter _audit;

    public DecideImpersonationCommandHandler(ICoreDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<bool> HandleAsync(DecideImpersonationCommand cmd, CancellationToken ct)
    {
        var grant = await _db.ImpersonationGrants.FirstOrDefaultAsync(g => g.Id == cmd.GrantId, ct);
        if (grant is null) return false;

        if (grant.Status != ImpersonationGrantStatus.Pending)
            throw new ValidationException(new Dictionary<string, string[]>
            { ["status"] = [$"This request is already {grant.Status} and cannot be decided again."] });

        var now = DateTimeOffset.UtcNow;

        if (!cmd.Request.Approve)
        {
            grant.Status = ImpersonationGrantStatus.Denied;
            grant.UpdatedAt = now;
            grant.UpdatedBy = cmd.ActorId;
            grant.RevokeReason = cmd.Request.Reason;   // reused as "why not"
            await _db.SaveChangesAsync(ct);

            await _audit.WriteAsync("impersonation.denied", "impersonation_grant", grant.Id,
                grant.Reason, newValues: new { deniedBy = cmd.ActorId }, ct: ct);
            return true;
        }

        // Rule 1 — never widen. Asking for read-only and being handed read-write means the consent
        // screen described a different thing from what was granted.
        var scope = cmd.Request.Scope ?? grant.Scope;
        if (scope == ImpersonationScope.ReadWrite && grant.Scope == ImpersonationScope.ReadOnly)
            throw new ValidationException(new Dictionary<string, string[]>
            { ["scope"] = ["This request asked for read-only access; it cannot be approved for write."] });
        if (scope is not (ImpersonationScope.ReadOnly or ImpersonationScope.ReadWrite))
            throw new ValidationException(new Dictionary<string, string[]>
            { ["scope"] = ["Scope must be read_only or read_write."] });

        var ttl = cmd.Request.TtlMinutes ?? DefaultTtlMinutes;
        if (ttl < 1 || ttl > MaxTtlMinutes)
            throw new ValidationException(new Dictionary<string, string[]>
            { ["ttlMinutes"] = [$"Choose between 1 and {MaxTtlMinutes} minutes."] });

        grant.Status = ImpersonationGrantStatus.Approved;
        grant.Scope = scope;
        grant.ApprovedByUserId = cmd.ActorId;
        grant.ApprovedAt = now;
        grant.ExpiresAt = now.AddMinutes(ttl);
        grant.UpdatedAt = now;
        grant.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("impersonation.approved", "impersonation_grant", grant.Id,
            grant.Reason,
            newValues: new { approvedBy = cmd.ActorId, scope, expiresAt = grant.ExpiresAt },
            ct: ct);

        return true;
    }
}
