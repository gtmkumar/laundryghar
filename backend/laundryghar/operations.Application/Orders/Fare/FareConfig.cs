using System.Text.Json;
using laundryghar.SharedDataModel.Common;
using operations.Application.Common.Interfaces;
using operations.Application.Common.Settings;

namespace operations.Application.Orders.Fare;

/// <summary>
/// Reads the delivery fare configuration from the shared kernel.system_settings table
/// (category 'fare', key 'quote') — the same mechanism the Identity Settings panel uses for
/// rider payout. Resolution follows the scope precedence store → franchise → brand → platform
/// via <see cref="SettingsResolver"/>, so a store or franchise can override the brand's matrix.
/// Returns sensible defaults when no row exists. Deserialized with web (camelCase) options to
/// match the stored JSON.
/// </summary>
internal static class FareConfig
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<FareSettings> LoadAsync(
        IOperationsDbContext db, Guid brandId, CancellationToken ct,
        Guid? franchiseId = null, Guid? storeId = null)
    {
        var eff = await SettingsResolver.GetEffectiveAsync(
            db, brandId, franchiseId, storeId, "fare", "quote", ct);

        if (eff is null) return new FareSettings();
        try { return JsonSerializer.Deserialize<FareSettings>(eff.Value, Json) ?? new FareSettings(); }
        catch (JsonException) { return new FareSettings(); }
    }
}
