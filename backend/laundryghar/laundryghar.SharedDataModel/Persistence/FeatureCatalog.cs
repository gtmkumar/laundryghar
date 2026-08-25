using laundryghar.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary>
/// <see cref="IFeatureCatalog"/> over the permission → module → feature chain, with a short
/// in-process cache. Same trade-off, and the same reasoning, as <see cref="TokenVersionStore"/>: the
/// map is global and changes only when the catalogue is re-seeded, so a short TTL beats
/// cross-process invalidation at this scale.
///
/// <para>The whole map is loaded in ONE query and cached as a dictionary rather than caching per
/// permission. There are a few hundred permissions and this is only consulted on the denial path, so
/// one round trip per TTL is cheaper than a miss per distinct code — and it means a 402 decision
/// never fans out into N queries.</para>
/// </summary>
public sealed class FeatureCatalog : IFeatureCatalog
{
    private const string CacheKey = "featurecatalog:permission-to-feature";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly LaundryGharDbContext _db;
    private readonly IMemoryCache _cache;

    public FeatureCatalog(LaundryGharDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<string?> FeatureForPermissionAsync(string permissionCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(permissionCode)) return null;

        var map = await GetMapAsync(ct);
        return map.GetValueOrDefault(permissionCode);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetMapAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<IReadOnlyDictionary<string, string>>(CacheKey, out var cached) && cached is not null)
            return cached;

        try
        {
            // permissions.module_key is the single canonical owner of a permission; modules.feature_key
            // is the feature gating that module. A module with no feature (core) contributes nothing,
            // which is what leaves core permissions ungated.
            var rows = await _db.Permissions.AsNoTracking()
                .Where(p => p.ModuleKey != null)
                .Join(_db.Modules.AsNoTracking().Where(m => m.FeatureKey != null),
                      p => p.ModuleKey, m => m.Key,
                      (p, m) => new { p.Code, m.FeatureKey })
                .ToListAsync(ct);

            var map = rows
                .GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().FeatureKey!, StringComparer.OrdinalIgnoreCase);

            _cache.Set(CacheKey, (IReadOnlyDictionary<string, string>)map, Ttl);
            return map;
        }
        catch
        {
            // Fail OPEN, deliberately: this only ever decides whether a denial is reported as 402 or
            // 403. If the lookup breaks, the caller still gets the correct denial — just the less
            // specific one. Never turn a catalogue outage into a request failure.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
