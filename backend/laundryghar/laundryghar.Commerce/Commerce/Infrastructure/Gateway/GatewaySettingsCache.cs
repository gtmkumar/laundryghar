using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Commerce.Infrastructure.Gateway;

/// <summary>
/// Thread-safe, TTL-based cache for <see cref="PaymentGatewaySettings"/> loaded from
/// <c>kernel.system_settings</c>.
///
/// TTL default is 60 seconds — a good trade-off between freshness (an admin saves new
/// creds in the panel) and DB chattiness (avoid a query per payment request).
///
/// The cache is intentionally brand-agnostic for Commerce: Commerce always operates
/// within the RLS brand scope already set on the connection, so the settings row
/// returned is already brand-filtered by the interceptor.
/// </summary>
public sealed class GatewaySettingsCache
{
    private readonly IFieldCipher _cipher;
    private readonly TimeSpan _ttl;

    private PaymentGatewaySettings? _cached;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GatewaySettingsCache(IFieldCipher cipher, TimeSpan? ttl = null)
    {
        _cipher = cipher;
        _ttl    = ttl ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Returns a live (non-stale) <see cref="PaymentGatewaySettings"/> for the current brand.
    /// Refreshes from DB whenever the cache has expired.
    /// </summary>
    public async Task<PaymentGatewaySettings> GetAsync(LaundryGharDbContext db, CancellationToken ct)
    {
        if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt)
            return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _cached;

            var row = await db.SystemSettings
                .AsNoTracking()
                .Where(s => s.Category == "payment" && s.SettingKey == "gateway" && s.Status == "active")
                .OrderBy(s => s.BrandId == null)
                .FirstOrDefaultAsync(ct);

            _cached    = PaymentGatewaySettings.FromJson(row?.SettingValue, _cipher);
            _expiresAt = DateTimeOffset.UtcNow + _ttl;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Forces expiry so the next call re-fetches from DB.</summary>
    public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;
}
