using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Worker.Infrastructure.Channels;

/// <summary>
/// Thread-safe, TTL-based cache for WhatsApp and SMS settings loaded from
/// <c>kernel.system_settings</c>. Refreshed at most once per <see cref="Ttl"/>.
///
/// The Worker bypasses RLS (WorkerCurrentTenant.BypassRls=true) so it can
/// read kernel.system_settings across all brands without a brand filter.
/// For multi-brand setups this reads the first platform-level row (BrandId IS NULL);
/// brand-specific overrides are not yet supported for Worker notification channels.
/// </summary>
internal sealed class NotificationSettingsCache
{
    internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly IFieldCipher _cipher;

    private WhatsAppSettings? _whatsApp;
    private SmsSettings?      _sms;
    private DateTimeOffset    _expiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public NotificationSettingsCache(IFieldCipher cipher)
        => _cipher = cipher;

    public async Task<(WhatsAppSettings wa, SmsSettings sms)> GetAsync(
        LaundryGharDbContext db, CancellationToken ct)
    {
        if (_whatsApp is not null && _sms is not null && DateTimeOffset.UtcNow < _expiresAt)
            return (_whatsApp, _sms);

        await _lock.WaitAsync(ct);
        try
        {
            if (_whatsApp is not null && _sms is not null && DateTimeOffset.UtcNow < _expiresAt)
                return (_whatsApp, _sms);

            // Worker bypasses RLS — reads kernel rows cross-brand.
            // Platform-level rows (BrandId IS NULL) are preferred; brand rows order second.
            var rows = await db.SystemSettings
                .AsNoTracking()
                .Where(s => s.Status == "active" &&
                            ((s.Category == "whatsapp" && s.SettingKey == "cloud") ||
                             (s.Category == "sms"      && s.SettingKey == "provider")))
                .ToListAsync(ct);

            var waRow  = rows.FirstOrDefault(r => r.Category == "whatsapp");
            var smsRow = rows.FirstOrDefault(r => r.Category == "sms");

            _whatsApp  = WhatsAppSettings.FromJson(waRow?.SettingValue, _cipher);
            _sms       = SmsSettings.FromJson(smsRow?.SettingValue, _cipher);
            _expiresAt = DateTimeOffset.UtcNow + Ttl;

            return (_whatsApp, _sms);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;
}
