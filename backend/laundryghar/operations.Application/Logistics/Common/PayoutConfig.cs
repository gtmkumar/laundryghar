using System.Text.Json;
using laundryghar.SharedDataModel.Common;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Logistics.Common;

/// <summary>
/// Reads the brand's rider payout rates from the shared kernel.system_settings
/// table (category 'payout', key 'rider') — the same row the Identity Settings
/// panel writes. Returns sensible defaults when no row exists. Deserialized with
/// web (camelCase) options; the default reader is case-sensitive PascalCase and
/// would silently return defaults for the camelCase stored JSON.
/// </summary>
public static class PayoutConfig
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<RiderPayoutSettings> LoadAsync(IOperationsDbContext db, Guid brandId, CancellationToken ct)
    {
        var raw = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == "payout" && s.SettingKey == "rider" && s.Status == "active"
                     && (s.BrandId == brandId || s.BrandId == null))
            .OrderBy(s => s.BrandId == null)   // brand-specific row wins over a platform default
            .Select(s => s.SettingValue)
            .FirstOrDefaultAsync(ct);

        if (raw is null) return new RiderPayoutSettings();
        try { return JsonSerializer.Deserialize<RiderPayoutSettings>(raw, Json) ?? new RiderPayoutSettings(); }
        catch (JsonException) { return new RiderPayoutSettings(); }
    }
}
