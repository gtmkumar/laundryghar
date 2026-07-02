using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Persistence;
using operations.Application.Common.Interfaces;
using operations.Application.Common.Settings;
using operations.Application.Orders.Common;
using operations.Application.Orders.Fare;
using operations.Tests.Catalog.Import;
using Xunit;

namespace operations.Tests.Settings;

/// <summary>
/// Coverage for the #26 settings foundation: precedence resolution (store→franchise→brand→platform),
/// platform fallback, unset⇒null, the batch reader, the value codec round-trip, clamp validation,
/// the order-pricing resolver (incl. the GST-unregistered-franchise ⇒ 0 rule), and the fare-config
/// precedence retrofit. Runs against an in-memory operations context.
/// </summary>
public class SettingsResolverTests
{
    private const string Cat = SettingCategories.Orders;
    private const string Key = SettingKeys.TaxRatePercent;

    private static void AddSetting(
        LaundryGharDbContext raw, string scopeType, Guid? brandId, Guid? franchiseId, Guid? storeId,
        string category, string key, string jsonValue, string dataType, string? schema = null)
    {
        raw.SystemSettings.Add(new SystemSetting
        {
            Id = Guid.NewGuid(), ScopeType = scopeType,
            BrandId = brandId, FranchiseId = franchiseId, StoreId = storeId,
            Category = category, SettingKey = key, SettingValue = jsonValue, DataType = dataType,
            ValidationSchema = schema, Status = "active", Version = 1,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    // ── Precedence ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Store_row_wins_over_franchise_brand_platform()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var franchise = Guid.NewGuid(); var store = Guid.NewGuid();

        AddSetting(raw, "platform",  null,  null,      null,  Cat, Key, "18", "decimal");
        AddSetting(raw, "brand",     brand, null,      null,  Cat, Key, "20", "decimal");
        AddSetting(raw, "franchise", brand, franchise, null,  Cat, Key, "22", "decimal");
        AddSetting(raw, "store",     brand, null,      store, Cat, Key, "25", "decimal");
        await raw.SaveChangesAsync();

        var eff = await SettingsResolver.GetEffectiveAsync(db, brand, franchise, store, Cat, Key, default);
        Assert.NotNull(eff);
        Assert.Equal("store", eff!.SourceScope);
        Assert.Equal(25m, SettingValueCodec.TryDecimal(eff.Value));
    }

    [Fact]
    public async Task Franchise_row_wins_when_no_store_row()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var franchise = Guid.NewGuid(); var store = Guid.NewGuid();

        AddSetting(raw, "platform",  null,  null,      null, Cat, Key, "18", "decimal");
        AddSetting(raw, "brand",     brand, null,      null, Cat, Key, "20", "decimal");
        AddSetting(raw, "franchise", brand, franchise, null, Cat, Key, "22", "decimal");
        await raw.SaveChangesAsync();

        var eff = await SettingsResolver.GetEffectiveAsync(db, brand, franchise, store, Cat, Key, default);
        Assert.Equal("franchise", eff!.SourceScope);
        Assert.Equal(22m, SettingValueCodec.TryDecimal(eff.Value));
    }

    [Fact]
    public async Task Platform_default_is_used_when_only_platform_row_exists()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();

        AddSetting(raw, "platform", null, null, null, Cat, Key, "18", "decimal");
        await raw.SaveChangesAsync();

        var eff = await SettingsResolver.GetEffectiveAsync(db, brand, franchiseId: null, storeId: null, Cat, Key, default);
        Assert.Equal("platform", eff!.SourceScope);
        Assert.Equal(18m, SettingValueCodec.TryDecimal(eff.Value));
    }

    [Fact]
    public async Task Unset_key_resolves_to_null()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        await raw.SaveChangesAsync();

        var eff = await SettingsResolver.GetEffectiveAsync(
            db, Guid.NewGuid(), null, null, Cat, SettingKeys.MinOrderValue, default);
        Assert.Null(eff);

        var num = await SettingsResolver.GetDecimalAsync(
            db, Guid.NewGuid(), null, null, Cat, SettingKeys.MinOrderValue, default);
        Assert.Null(num);
    }

    [Fact]
    public async Task Another_brands_row_does_not_leak()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brandA = Guid.NewGuid(); var brandB = Guid.NewGuid();

        AddSetting(raw, "brand", brandB, null, null, Cat, Key, "99", "decimal");
        await raw.SaveChangesAsync();

        var eff = await SettingsResolver.GetEffectiveAsync(db, brandA, null, null, Cat, Key, default);
        Assert.Null(eff);   // brandA sees nothing; brandB's row must not resolve for brandA
    }

    // ── Batch reader ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Batch_reader_resolves_each_key_by_precedence_in_one_call()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var store = Guid.NewGuid();

        AddSetting(raw, "platform", null,  null, null,  Cat, SettingKeys.TaxRatePercent,          "18",    "decimal");
        AddSetting(raw, "brand",    brand, null, null,  Cat, SettingKeys.ExpressSurchargePercent, "40",    "decimal");
        AddSetting(raw, "store",    brand, null, store, Cat, SettingKeys.TaxRatePercent,          "5",     "decimal");
        AddSetting(raw, "brand",    brand, null, null,  Cat, SettingKeys.CurrencyCode,            "\"AED\"", "string");
        await raw.SaveChangesAsync();

        var eff = await SettingsResolver.GetEffectiveBatchAsync(
            db, brand, null, store, Cat,
            [SettingKeys.TaxRatePercent, SettingKeys.ExpressSurchargePercent, SettingKeys.CurrencyCode, SettingKeys.MinOrderValue],
            default);

        Assert.Equal("store", eff[SettingKeys.TaxRatePercent].SourceScope);
        Assert.Equal(5m, SettingValueCodec.TryDecimal(eff[SettingKeys.TaxRatePercent].Value));
        Assert.Equal(40m, SettingValueCodec.TryDecimal(eff[SettingKeys.ExpressSurchargePercent].Value));
        Assert.Equal("AED", SettingValueCodec.DecodeString(eff[SettingKeys.CurrencyCode].Value));
        Assert.False(eff.ContainsKey(SettingKeys.MinOrderValue));   // unset key omitted
    }

    // ── Codec round-trip ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("18", "decimal", "18")]
    [InlineData("48", "int", "48")]
    [InlineData("true", "bool", "true")]
    [InlineData("INR", "string", "INR")]
    public void Codec_encodes_then_decodes_to_the_original_display_value(string raw, string dtype, string expected)
    {
        var encoded = SettingValueCodec.Encode(raw, dtype);
        Assert.Equal(expected, SettingValueCodec.DecodeString(encoded));
    }

    [Fact]
    public void Codec_rejects_malformed_scalar()
    {
        Assert.Throws<FormatException>(() => SettingValueCodec.Encode("not-a-number", "decimal"));
    }

    // ── Clamp validation ─────────────────────────────────────────────────────────

    [Fact]
    public void Clamp_within_band_is_accepted()
    {
        var schema = SettingClampSchema.Parse("{\"min\":10,\"max\":20}");
        Assert.Null(SettingClampSchema.Validate(schema, "15", "decimal"));
    }

    [Fact]
    public void Clamp_outside_band_is_rejected()
    {
        var schema = SettingClampSchema.Parse("{\"min\":10,\"max\":20}");
        Assert.NotNull(SettingClampSchema.Validate(schema, "25", "decimal"));
        Assert.NotNull(SettingClampSchema.Validate(schema, "5", "decimal"));
    }

    [Fact]
    public void No_clamp_schema_means_any_value_is_free()
    {
        Assert.Null(SettingClampSchema.Validate(null, "9999", "decimal"));
    }

    [Fact]
    public void Clamp_allowed_list_restricts_string_values()
    {
        var schema = SettingClampSchema.Parse("{\"allowed\":[\"INR\",\"AED\"]}");
        Assert.Null(SettingClampSchema.Validate(schema, "INR", "string"));
        Assert.NotNull(SettingClampSchema.Validate(schema, "USD", "string"));
    }

    // ── Order-pricing resolver (the CreateOrder seam) ────────────────────────────

    [Fact]
    public async Task OrderSettings_falls_back_to_appsettings_when_no_rows()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        await raw.SaveChangesAsync();
        var fallback = new OrdersSettings();   // 18 / 50 / 48 / 24 / INR

        var r = await OrderSettingsResolver.ResolveAsync(
            db, Guid.NewGuid(), null, null, franchiseIsGstRegistered: true, fallback, default);

        Assert.Equal(18m, r.TaxRatePercent);
        Assert.Equal(50m, r.ExpressSurchargePercent);
        Assert.Equal(48, r.DefaultTatHours);
        Assert.Equal(24, r.ExpressTatHours);
        Assert.Equal("INR", r.CurrencyCode);
    }

    [Fact]
    public async Task OrderSettings_uses_resolved_row_over_fallback()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        AddSetting(raw, "brand", brand, null, null, Cat, SettingKeys.TaxRatePercent, "12", "decimal");
        await raw.SaveChangesAsync();

        var r = await OrderSettingsResolver.ResolveAsync(
            db, brand, null, null, franchiseIsGstRegistered: true, new OrdersSettings(), default);

        Assert.Equal(12m, r.TaxRatePercent);
    }

    [Fact]
    public async Task Unregistered_franchise_forces_tax_rate_to_zero()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        // Even with a configured 18% brand rate, an unregistered franchise charges no GST.
        AddSetting(raw, "brand", brand, null, null, Cat, SettingKeys.TaxRatePercent, "18", "decimal");
        await raw.SaveChangesAsync();

        var r = await OrderSettingsResolver.ResolveAsync(
            db, brand, null, null, franchiseIsGstRegistered: false, new OrdersSettings(), default);

        Assert.Equal(0m, r.TaxRatePercent);
    }

    // ── Fare-config precedence retrofit ──────────────────────────────────────────

    [Fact]
    public async Task Fare_config_store_row_overrides_brand()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var store = Guid.NewGuid();

        AddSetting(raw, "brand", brand, null, null,  "fare", "quote", "{\"quoteTtlSeconds\":100}", "object");
        AddSetting(raw, "store", brand, null, store, "fare", "quote", "{\"quoteTtlSeconds\":300}", "object");
        await raw.SaveChangesAsync();

        var storeFare = await FareConfig.LoadAsync(db, brand, default, franchiseId: null, storeId: store);
        Assert.Equal(300, storeFare.QuoteTtlSeconds);

        var brandFare = await FareConfig.LoadAsync(db, brand, default);   // no store → brand row
        Assert.Equal(100, brandFare.QuoteTtlSeconds);
    }
}
