using laundryghar.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary>
/// <see cref="IBrandStatusStore"/> with a short in-process cache. The TTL is the ceiling on how long
/// a suspension takes to bite across processes — and, just as importantly, on how long a
/// REINSTATEMENT takes to restore service after an owner pays. Kept short for the second reason:
/// nobody minds waiting to be cut off, everybody minds waiting to be let back in.
/// </summary>
public sealed class BrandStatusStore : IBrandStatusStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly LaundryGharDbContext _db;
    private readonly IMemoryCache _cache;

    public BrandStatusStore(LaundryGharDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<string?> GetStatusAsync(Guid brandId, CancellationToken ct = default)
    {
        var key = $"brandstatus:{brandId}";
        if (_cache.TryGetValue<string>(key, out var cached) && cached is not null) return cached;

        try
        {
            // Goes through kernel.brand_status (migration 0009), NOT a direct read of
            // tenancy_org.brands. That table carries `rls_admin_only USING (rls_bypass())`, so
            // ordinary tenant traffic — which is exactly who this gate exists to stop — reads it as
            // EMPTY. An EF read here returned null for every tenant request and, because this store
            // fails open, the suspension gate silently never fired while every unit test passed.
            // The SECURITY DEFINER function grants this one lookup instead of a request-wide bypass.
            var rows = await _db.Database
                .SqlQuery<string?>($"SELECT kernel.brand_status({brandId}) AS \"Value\"")
                .ToListAsync(ct);
            var status = rows.Count > 0 ? rows[0] : null;

            if (!string.IsNullOrEmpty(status)) _cache.Set(key, status, Ttl);
            return status;
        }
        catch
        {
            // Fail OPEN. A status lookup that errors must never take a working tenant offline —
            // the worst case of guessing wrong here is a suspended brand keeping service briefly,
            // which is far better than an outage for every healthy one.
            return null;
        }
    }

    public async Task<string?> SetCancellationStateAsync(
        Guid brandId, string status, CancellationToken ct = default)
    {
        var rows = await _db.Database
            .SqlQuery<string?>($"SELECT kernel.set_brand_cancellation_state({brandId}, {status}) AS \"Value\"")
            .ToListAsync(ct);

        // Evict rather than overwrite. Overwriting would cache a value this process believes without
        // having read it back, and the one thing worse than a stale suspension state is a confidently
        // wrong one.
        _cache.Remove($"brandstatus:{brandId}");

        return rows.Count > 0 ? rows[0] : null;
    }
}
