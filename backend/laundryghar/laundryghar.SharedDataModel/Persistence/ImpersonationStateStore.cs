using laundryghar.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary>
/// <see cref="IImpersonationStateStore"/> over <c>kernel.impersonation_grant_state</c> (migration
/// 0014).
///
/// <para>Two decisions worth stating, because both are the opposite of <see cref="BrandStatusStore"/>:</para>
/// <list type="number">
/// <item><b>No cache.</b> See the interface — a TTL here is a window in which revocation has not
/// happened yet.</item>
/// <item><b>Fails CLOSED.</b> Brand status fails open because guessing wrong there takes healthy
/// tenants offline. Here, guessing wrong leaves a support engineer inside a customer's account
/// without provable consent. When the oracle cannot answer, the answer is no.</item>
/// </list>
///
/// <para>The SECURITY DEFINER function is what makes this readable at all: the request has not yet
/// been allowed to see anything, and <c>impersonation_grants</c> is RLS-scoped by brand.</para>
/// </summary>
public sealed class ImpersonationStateStore : IImpersonationStateStore
{
    private readonly LaundryGharDbContext _db;

    public ImpersonationStateStore(LaundryGharDbContext db) => _db = db;

    private sealed record Row(string Status, string Scope, Guid BrandId, Guid SupportUserId, DateTimeOffset? ExpiresAt);

    public async Task<ImpersonationState?> GetAsync(Guid grantId, CancellationToken ct = default)
    {
        var rows = await _db.Database.SqlQuery<Row>($"""
            SELECT status        AS "Status",
                   scope         AS "Scope",
                   brand_id      AS "BrandId",
                   support_user_id AS "SupportUserId",
                   expires_at    AS "ExpiresAt"
            FROM kernel.impersonation_grant_state({grantId})
            """).ToListAsync(ct);

        if (rows.Count == 0) return null;
        var r = rows[0];
        return new ImpersonationState(r.Status, r.Scope, r.BrandId, r.SupportUserId, r.ExpiresAt);
    }

    public async Task<Guid?> RequestAsync(
        Guid brandId, Guid supportUserId, string reason, string scope, CancellationToken ct = default)
    {
        try
        {
            var ids = await _db.Database.SqlQuery<Guid>(
                $"SELECT kernel.request_impersonation({brandId}, {supportUserId}, {reason}, {scope}) AS \"Value\"")
                .ToListAsync(ct);
            return ids.Count > 0 ? ids[0] : null;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "P0002")
        {
            // no_data_found — the function's way of saying "unknown brand".
            return null;
        }
    }
}
