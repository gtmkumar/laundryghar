using System.Text.Json;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Orders.Application.Fare;

/// <summary>
/// Reads the brand's delivery fare configuration from the shared kernel.system_settings
/// table (category 'fare', key 'quote') — the same mechanism the Identity Settings panel
/// uses for rider payout. Returns sensible defaults when no row exists. Deserialized with
/// web (camelCase) options to match the stored JSON.
/// </summary>
internal static class FareConfig
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<FareSettings> LoadAsync(LaundryGharDbContext db, Guid brandId, CancellationToken ct)
    {
        var raw = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == "fare" && s.SettingKey == "quote" && s.Status == "active"
                     && (s.BrandId == brandId || s.BrandId == null))
            .OrderBy(s => s.BrandId == null)   // brand-specific row wins over a platform default
            .Select(s => s.SettingValue)
            .FirstOrDefaultAsync(ct);

        if (raw is null) return new FareSettings();
        try { return JsonSerializer.Deserialize<FareSettings>(raw, Json) ?? new FareSettings(); }
        catch (JsonException) { return new FareSettings(); }
    }
}
