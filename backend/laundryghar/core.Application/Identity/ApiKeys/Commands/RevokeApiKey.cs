using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth.Audit;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.ApiKeys.Commands;

public sealed record RevokeApiKeyCommand(Guid BrandId, Guid KeyId, Guid? ActorId) : ICommand<bool>;

/// <summary>
/// Kills a credential. Takes effect on the very next request, because <c>ApiKeyStore</c> is
/// deliberately uncached — "we revoked it, it stops working within a minute" is not an answer anyone
/// wants to give after a key turns up in a public repository.
///
/// <para>The row is kept, not deleted. Its usage history is the record of what the key did before it
/// was revoked, and that is exactly what gets read after a leak.</para>
/// </summary>
public sealed class RevokeApiKeyCommandHandler : ICommandHandler<RevokeApiKeyCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly IAuditWriter _audit;

    public RevokeApiKeyCommandHandler(ICoreDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<bool> HandleAsync(RevokeApiKeyCommand cmd, CancellationToken ct)
    {
        var key = await _db.ApiKeys.FirstOrDefaultAsync(
            k => k.Id == cmd.KeyId && k.BrandId == cmd.BrandId, ct);
        if (key is null) return false;

        // Already revoked — report success. The caller's intent ("this must not work") is satisfied,
        // and an error here would make a panicked double-click look like a failure to revoke.
        if (key.Status == ApiKeyStatus.Revoked) return true;

        var now = DateTimeOffset.UtcNow;
        key.Status = ApiKeyStatus.Revoked;
        key.RevokedAt = now;
        key.RevokedByUserId = cmd.ActorId;
        key.UpdatedAt = now;
        key.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("api_key.revoked", "api_key", key.Id, key.KeyPrefix,
            newValues: new { revokedBy = cmd.ActorId }, ct: ct);

        return true;
    }
}
