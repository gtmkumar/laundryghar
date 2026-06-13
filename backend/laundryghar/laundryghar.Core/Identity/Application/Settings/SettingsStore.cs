using System.Text.Json;
using laundryghar.Identity.Infrastructure.Email;
using laundryghar.Identity.Infrastructure.Services;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Identity.Application.Settings;

/// <summary>Persisted map-provider config (stored as JSON under category 'maps', key 'provider').</summary>
public sealed class MapsSettings
{
    public string Provider { get; set; } = "osm";   // osm | google | mapbox
    public string? GoogleApiKey { get; set; }
    public string? MapboxToken { get; set; }
}

/// <summary>
/// Helpers for reading and upserting brand-scoped rows in
/// <c>kernel.system_settings</c>. The RLS connection interceptor scopes every
/// query to the request's brand, so lookups are by (category, key) only.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The brand these settings belong to — the caller's brand, or the only brand for a platform admin.</summary>
    public static async Task<Guid?> ResolveBrandIdAsync(ICurrentUser user, LaundryGharDbContext db, CancellationToken ct)
    {
        if (user.BrandId is Guid b) return b;
        return await db.Brands.AsNoTracking().OrderBy(x => x.CreatedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
    }

    public static Task<SystemSetting?> FindAsync(LaundryGharDbContext db, Guid? brandId, string category, string key, CancellationToken ct)
    {
        var q = db.SystemSettings.Where(s => s.Category == category && s.SettingKey == key && s.Status == "active");
        if (brandId.HasValue) q = q.Where(s => s.BrandId == brandId);
        return q.OrderBy(s => s.BrandId == null).FirstOrDefaultAsync(ct);
    }

    public static async Task<EmailSettings> LoadEmailAsync(LaundryGharDbContext db, Guid? brandId, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, "email", "smtp", ct);
        if (row is null) return new EmailSettings();
        try { return JsonSerializer.Deserialize<EmailSettings>(row.SettingValue) ?? new EmailSettings(); }
        catch (JsonException) { return new EmailSettings(); }
    }

    public static async Task<string> LoadProvisioningModeAsync(LaundryGharDbContext db, Guid? brandId, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, "provisioning", "invite", ct);
        if (row is null) return "admin_activate";
        try
        {
            using var doc = JsonDocument.Parse(row.SettingValue);
            return doc.RootElement.TryGetProperty("mode", out var m) ? m.GetString() ?? "admin_activate" : "admin_activate";
        }
        catch (JsonException) { return "admin_activate"; }
    }

    public static async Task<string> LoadAdminBaseUrlAsync(LaundryGharDbContext db, Guid? brandId, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, "app", "urls", ct);
        const string fallback = "http://localhost:5173";
        if (row is null) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(row.SettingValue);
            return doc.RootElement.TryGetProperty("adminBaseUrl", out var u) ? (u.GetString() ?? fallback) : fallback;
        }
        catch (JsonException) { return fallback; }
    }

    public static async Task<MapsSettings> LoadMapsAsync(LaundryGharDbContext db, Guid? brandId, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, "maps", "provider", ct);
        if (row is null) return new MapsSettings();
        // Deserialize with the SAME web options used to serialize (UpsertAsync) — the
        // stored JSON is camelCase, and the default (case-sensitive PascalCase) reader
        // silently fails to bind and returns the osm default.
        try { return JsonSerializer.Deserialize<MapsSettings>(row.SettingValue, Json) ?? new MapsSettings(); }
        catch (JsonException) { return new MapsSettings(); }
    }

    public static async Task<RiderPayoutSettings> LoadPayoutAsync(LaundryGharDbContext db, Guid? brandId, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, "payout", "rider", ct);
        if (row is null) return new RiderPayoutSettings();
        // Web options (camelCase, case-insensitive) — see LoadMapsAsync note.
        try { return JsonSerializer.Deserialize<RiderPayoutSettings>(row.SettingValue, Json) ?? new RiderPayoutSettings(); }
        catch (JsonException) { return new RiderPayoutSettings(); }
    }

    /// <summary>
    /// Loads payment gateway settings, decrypting secret fields with <paramref name="cipher"/>.
    /// Returns a default (disabled) instance when no row exists.
    /// </summary>
    public static async Task<PaymentGatewaySettings> LoadPaymentGatewayAsync(
        LaundryGharDbContext db, Guid? brandId, IFieldCipher cipher, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, "payment", "gateway", ct);
        return PaymentGatewaySettings.FromJson(row?.SettingValue, cipher);
    }

    /// <summary>
    /// Loads WhatsApp settings, decrypting the access token with <paramref name="cipher"/>.
    /// Returns a default (disabled) instance when no row exists.
    /// </summary>
    public static async Task<WhatsAppSettings> LoadWhatsAppAsync(
        LaundryGharDbContext db, Guid? brandId, IFieldCipher cipher, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, "whatsapp", "cloud", ct);
        return WhatsAppSettings.FromJson(row?.SettingValue, cipher);
    }

    /// <summary>
    /// Loads SMS settings, decrypting the auth key with <paramref name="cipher"/>.
    /// Returns a default (disabled) instance when no row exists.
    /// </summary>
    public static async Task<SmsSettings> LoadSmsAsync(
        LaundryGharDbContext db, Guid? brandId, IFieldCipher cipher, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, "sms", "provider", ct);
        return SmsSettings.FromJson(row?.SettingValue, cipher);
    }

    // ── Masking helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns "••••" + the last 4 characters of <paramref name="plaintext"/>,
    /// or null when the value is null/empty.
    /// </summary>
    public static string? MaskSecret(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        return plaintext.Length >= 4
            ? $"••••{plaintext[^4..]}"
            : "••••";
    }

    /// <summary>Upsert a setting's JSON value, creating the row if it does not exist yet.</summary>
    public static async Task UpsertAsync(
        LaundryGharDbContext db, Guid? brandId, string category, string key,
        object value, bool isEncrypted, Guid? actorId, CancellationToken ct)
    {
        var row = await FindAsync(db, brandId, category, key, ct);
        var jsonValue = JsonSerializer.Serialize(value, Json);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.NewGuid(), ScopeType = brandId.HasValue ? "brand" : "platform", BrandId = brandId,
                Category = category, SettingKey = key, SettingValue = jsonValue, DataType = "object",
                IsEncrypted = isEncrypted, Status = "active", Version = 1,
                CreatedAt = now, UpdatedAt = now, CreatedBy = actorId, UpdatedBy = actorId,
            });
        }
        else
        {
            row.SettingValue = jsonValue;
            row.IsEncrypted = isEncrypted;
            row.UpdatedAt = now;
            row.UpdatedBy = actorId;
            row.Version++;
        }
        await db.SaveChangesAsync(ct);
    }
}
