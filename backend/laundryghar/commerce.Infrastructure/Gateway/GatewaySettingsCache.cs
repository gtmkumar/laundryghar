using System.Collections.Concurrent;
using commerce.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using Microsoft.EntityFrameworkCore;

namespace commerce.Infrastructure.Gateway;

/// <summary>
/// Thread-safe, TTL-based, PER-BRAND cache for <see cref="PaymentGatewaySettings"/> loaded
/// from <c>kernel.system_settings</c>.
///
/// TTL default is 60 seconds — a good trade-off between freshness (an admin saves new
/// creds in the panel) and DB chattiness (avoid a query per payment request).
///
/// SECURITY (SEC-2): the cache is keyed by <c>brandId</c>. A single decrypted Razorpay
/// key/secret/webhook-secret is NEVER shared across brands. In a multi-brand deployment,
/// brand B's payment within the TTL window must transact against brand B's own credentials,
/// not whatever brand happened to prime the cache last. Each brand gets its own cache slot
/// with its own independent 60s TTL. The "global config" row (BrandId == null) is cached
/// under its own well-known sentinel key and is only ever served when the brand-specific
/// row is absent — see the resolution comment in the query below.
/// </summary>
public sealed class GatewaySettingsCache : IGatewaySettingsCache
{
    /// <summary>
    /// Sentinel cache key for the global (BrandId == null) settings row. A real brandId is a
    /// random GUID, so <see cref="Guid.Empty"/> can never collide with a brand slot.
    /// </summary>
    private static readonly Guid GlobalKey = Guid.Empty;

    private readonly IFieldCipher _cipher;
    private readonly TimeSpan _ttl;

    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    // One lock per brand key keeps refreshes serialized per-brand without cross-brand contention.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    private sealed record CacheEntry(PaymentGatewaySettings Settings, DateTimeOffset ExpiresAt);

    public GatewaySettingsCache(IFieldCipher cipher, TimeSpan? ttl = null)
    {
        _cipher = cipher;
        _ttl    = ttl ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Returns a live (non-stale) <see cref="PaymentGatewaySettings"/> for the given
    /// <paramref name="brandId"/>. Refreshes that brand's slot from DB when expired.
    ///
    /// Resolution per brand: prefer the brand-specific row, fall back to the global config
    /// row (BrandId == null). The original <c>OrderBy(s =&gt; s.BrandId == null)</c> ordered
    /// brand-specific rows (false) before the global row (true) so that <c>FirstOrDefault</c>
    /// returns the brand row when present — preserved here, but now constrained to THIS brand.
    /// </summary>
    /// <param name="brandId">
    /// The brand whose gateway settings to resolve. <c>null</c> resolves the global config row
    /// only (used by lanes that have no brand context, e.g. an as-yet-unresolved fallback).
    /// </param>
    public async Task<PaymentGatewaySettings> GetAsync(
        ICommerceDbContext db, Guid? brandId, CancellationToken ct)
    {
        var key = brandId ?? GlobalKey;

        if (_entries.TryGetValue(key, out var fresh) && DateTimeOffset.UtcNow < fresh.ExpiresAt)
            return fresh.Settings;

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the per-brand lock.
            if (_entries.TryGetValue(key, out var again) && DateTimeOffset.UtcNow < again.ExpiresAt)
                return again.Settings;

            // Brand-specific row OR the global (BrandId == null) row — scoped to THIS brand.
            // Order so the brand-specific row (BrandId == brandId) wins over the global row.
            var query = db.SystemSettings
                .AsNoTracking()
                .Where(s => s.Category == "payment" && s.SettingKey == "gateway" && s.Status == "active");

            query = brandId is null
                ? query.Where(s => s.BrandId == null)
                : query.Where(s => s.BrandId == brandId || s.BrandId == null)
                       .OrderBy(s => s.BrandId == null); // brand-specific (false) before global (true)

            var row = await query.FirstOrDefaultAsync(ct);

            var settings = PaymentGatewaySettings.FromJson(row?.SettingValue, _cipher);
            _entries[key] = new CacheEntry(settings, DateTimeOffset.UtcNow + _ttl);
            return settings;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forces expiry of every cached brand slot so the next call re-fetches from DB.</summary>
    public void Invalidate() => _entries.Clear();

    /// <summary>Forces expiry of a single brand's slot.</summary>
    public void Invalidate(Guid? brandId) => _entries.TryRemove(brandId ?? GlobalKey, out _);
}
