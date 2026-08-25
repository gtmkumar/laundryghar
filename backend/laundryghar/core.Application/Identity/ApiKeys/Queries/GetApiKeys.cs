using core.Application.Common.Interfaces;
using core.Application.Identity.ApiKeys.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.ApiKeys.Queries;

public sealed record GetApiKeysQuery(Guid BrandId) : IQuery<IReadOnlyList<ApiKeyDto>>;

/// <summary>
/// The provider's key console, with 30 days of metering alongside each key.
///
/// <para>Revoked keys are listed too. "What did this key do before we killed it" is the question
/// asked after a leak, and hiding the row hides the usage history that answers it.</para>
/// </summary>
public sealed class GetApiKeysQueryHandler : IQueryHandler<GetApiKeysQuery, IReadOnlyList<ApiKeyDto>>
{
    private readonly ICoreDbContext _db;
    public GetApiKeysQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<ApiKeyDto>> HandleAsync(GetApiKeysQuery query, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

        var usage = await _db.ApiKeyUsages.AsNoTracking()
            .Where(u => u.BrandId == query.BrandId && u.UsageDate >= since)
            .GroupBy(u => u.ApiKeyId)
            .Select(g => new
            {
                KeyId = g.Key,
                Requests = g.Sum(u => u.RequestCount),
                Errors = g.Sum(u => u.ErrorCount),
            })
            .ToDictionaryAsync(x => x.KeyId, ct);

        var keys = await _db.ApiKeys.AsNoTracking()
            .Where(k => k.BrandId == query.BrandId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return keys.Select(k => new ApiKeyDto(
            k.Id, k.Name, k.KeyPrefix, k.Environment, k.Scopes, k.Status,
            k.RateLimitPerMinute, k.ExpiresAt, k.LastUsedAt, k.RevokedAt, k.CreatedAt,
            usage.TryGetValue(k.Id, out var u) ? u.Requests : 0,
            usage.TryGetValue(k.Id, out var e) ? e.Errors : 0)).ToList();
    }
}
