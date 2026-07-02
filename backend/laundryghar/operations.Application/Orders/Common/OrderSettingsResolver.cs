using operations.Application.Common.Interfaces;
using operations.Application.Common.Settings;

namespace operations.Application.Orders.Common;

/// <summary>
/// The order-pricing business rules resolved for a specific order's scope, with the appsettings
/// <see cref="OrdersSettings"/> as the belt-and-braces fallback when no <c>system_settings</c> row
/// exists at any scope. This is the seam <see cref="Commands.CreateOrderHandler"/> reads instead of
/// the hardcoded config, so HQ/franchise/store can tune tax, surcharge, TAT and currency per scope.
/// </summary>
public sealed record ResolvedOrderSettings(
    decimal TaxRatePercent,
    decimal ExpressSurchargePercent,
    int DefaultTatHours,
    int ExpressTatHours,
    string CurrencyCode);

public static class OrderSettingsResolver
{
    private static readonly string[] Keys =
    [
        SettingKeys.TaxRatePercent,
        SettingKeys.ExpressSurchargePercent,
        SettingKeys.DefaultTatHours,
        SettingKeys.ExpressTatHours,
        SettingKeys.CurrencyCode,
    ];

    /// <param name="franchiseIsGstRegistered">
    /// False when the order's franchise has no GSTIN (unregistered). An unregistered franchise must
    /// not charge GST, so the effective tax rate is forced to 0 regardless of any configured value.
    /// </param>
    public static async Task<ResolvedOrderSettings> ResolveAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        bool franchiseIsGstRegistered, OrdersSettings fallback, CancellationToken ct)
    {
        var eff = await SettingsResolver.GetEffectiveBatchAsync(
            db, brandId, franchiseId, storeId, SettingCategories.Orders, Keys, ct);

        var taxRate = Decimal(eff, SettingKeys.TaxRatePercent) ?? fallback.TaxRatePercent;
        if (!franchiseIsGstRegistered) taxRate = 0m;   // unregistered franchise ⇒ no GST

        return new ResolvedOrderSettings(
            TaxRatePercent:          taxRate,
            ExpressSurchargePercent: Decimal(eff, SettingKeys.ExpressSurchargePercent) ?? fallback.ExpressSurchargePercent,
            DefaultTatHours:         Int(eff, SettingKeys.DefaultTatHours)            ?? fallback.DefaultTatHours,
            ExpressTatHours:         Int(eff, SettingKeys.ExpressTatHours)            ?? fallback.ExpressTatHours,
            CurrencyCode:            String(eff, SettingKeys.CurrencyCode)            ?? fallback.DefaultCurrencyCode);
    }

    private static decimal? Decimal(IReadOnlyDictionary<string, EffectiveSetting> eff, string key)
        => eff.TryGetValue(key, out var v) ? SettingValueCodec.TryDecimal(v.Value) : null;

    private static int? Int(IReadOnlyDictionary<string, EffectiveSetting> eff, string key)
        => eff.TryGetValue(key, out var v) ? SettingValueCodec.TryInt(v.Value) : null;

    private static string? String(IReadOnlyDictionary<string, EffectiveSetting> eff, string key)
        => eff.TryGetValue(key, out var v) ? SettingValueCodec.DecodeString(v.Value) : null;
}
