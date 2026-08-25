using laundryghar.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary><see cref="IApiKeyStore"/> over the two SECURITY DEFINER functions in migration 0016.
/// A request presenting an API key has no session, no brand and no RLS reach — resolving the key is
/// what establishes them.</summary>
public sealed class ApiKeyStore : IApiKeyStore
{
    private readonly LaundryGharDbContext _db;

    public ApiKeyStore(LaundryGharDbContext db) => _db = db;

    private sealed record Row(
        Guid Id, Guid BrandId, string SecretHash, string[] Scopes, string Status,
        string Environment, int? RateLimitPerMinute, DateTimeOffset? ExpiresAt, bool Entitled);

    public async Task<ApiKeyRecord?> ResolveAsync(string prefix, CancellationToken ct = default)
    {
        var rows = await _db.Database.SqlQuery<Row>($"""
            SELECT id                    AS "Id",
                   brand_id              AS "BrandId",
                   secret_hash           AS "SecretHash",
                   scopes                AS "Scopes",
                   status                AS "Status",
                   environment           AS "Environment",
                   rate_limit_per_minute AS "RateLimitPerMinute",
                   expires_at            AS "ExpiresAt",
                   entitled              AS "Entitled"
            FROM kernel.resolve_api_key({prefix})
            """).ToListAsync(ct);

        if (rows.Count == 0) return null;
        var r = rows[0];
        return new ApiKeyRecord(r.Id, r.BrandId, r.SecretHash, r.Scopes, r.Status,
                                r.Environment, r.RateLimitPerMinute, r.ExpiresAt, r.Entitled);
    }

    public async Task RecordUseAsync(Guid keyId, Guid brandId, bool isError, CancellationToken ct = default)
    {
        try
        {
            await _db.Database.ExecuteSqlAsync(
                $"SELECT kernel.record_api_key_use({keyId}, {brandId}, {isError})", ct);
        }
        catch
        {
            // Swallowed on purpose. Metering is bookkeeping; failing a request the caller was
            // entitled to make because a counter could not be incremented would be the tail wagging
            // the dog. A gap in a usage chart is recoverable, a 500 on a customer's integration is not.
        }
    }
}
