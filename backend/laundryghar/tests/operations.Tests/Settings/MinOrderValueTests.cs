using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using Microsoft.Extensions.Options;
using operations.Application.Catalog.Customer.Self.Queries;
using operations.Application.Common.Settings;
using operations.Application.Orders.Common;
using operations.Tests.Catalog.Import;
using Xunit;

namespace operations.Tests.Settings;

/// <summary>
/// Coverage for #23 minimum-order-value: the <see cref="MinOrderValueRule"/> hard block invoked
/// by order + pickup creation (below / at / above / unset / store-override precedence) and the
/// customer catalog-config query handler (set / unset / store override). Runs against the
/// in-memory operations context, mirroring the Settings/Import adapter pattern.
/// </summary>
public class MinOrderValueTests
{
    private static void AddSetting(
        LaundryGharDbContext raw, string scopeType, Guid? brandId, Guid? franchiseId, Guid? storeId,
        string category, string key, string jsonValue, string dataType)
    {
        raw.SystemSettings.Add(new SystemSetting
        {
            Id = Guid.NewGuid(), ScopeType = scopeType,
            BrandId = brandId, FranchiseId = franchiseId, StoreId = storeId,
            Category = category, SettingKey = key, SettingValue = jsonValue, DataType = dataType,
            Status = "active", Version = 1,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static void AddStore(LaundryGharDbContext raw, Guid brandId, Guid franchiseId, Guid storeId)
    {
        raw.Stores.Add(new Store
        {
            Id = storeId, BrandId = brandId, FranchiseId = franchiseId,
            Code = "S1", Name = "Test Store", StoreType = "store",
            AddressLine1 = "1 Test Rd", City = "Test", State = "TS", Pincode = "110001",
            CountryCode = "IN", Timezone = "Asia/Kolkata", Status = "active",
            CurrencyCode = "INR", Config = "{}",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    // ── MinOrderValueRule (the create-order / pickup seam) ───────────────────────

    [Fact]
    public async Task Rule_passes_when_min_order_value_is_unset()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        await raw.SaveChangesAsync();

        // No exception ⇒ no restriction when the key is unset at every scope.
        await MinOrderValueRule.EnforceAsync(db, Guid.NewGuid(), null, null, subtotal: 1m, default);
    }

    [Fact]
    public async Task Rule_throws_when_subtotal_below_minimum()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        AddSetting(raw, "brand", brand, null, null, SettingCategories.Orders, SettingKeys.MinOrderValue, "500", "decimal");
        await raw.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<StructuredBusinessRuleException>(() =>
            MinOrderValueRule.EnforceAsync(db, brand, null, null, subtotal: 350m, default));

        Assert.Equal(MinOrderValueRule.ErrorCode, ex.Code);
        Assert.Equal("500", ex.Fields["minimum"]);
        Assert.Equal("350", ex.Fields["subtotal"]);
        Assert.Equal("150", ex.Fields["shortfall"]);
    }

    [Fact]
    public async Task Rule_passes_when_subtotal_equals_minimum()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        AddSetting(raw, "brand", brand, null, null, SettingCategories.Orders, SettingKeys.MinOrderValue, "500", "decimal");
        await raw.SaveChangesAsync();

        // Boundary: exactly at the minimum is allowed (>= comparison).
        await MinOrderValueRule.EnforceAsync(db, brand, null, null, subtotal: 500m, default);
    }

    [Fact]
    public async Task Rule_passes_when_subtotal_above_minimum()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        AddSetting(raw, "brand", brand, null, null, SettingCategories.Orders, SettingKeys.MinOrderValue, "500", "decimal");
        await raw.SaveChangesAsync();

        await MinOrderValueRule.EnforceAsync(db, brand, null, null, subtotal: 750m, default);
    }

    [Fact]
    public async Task Rule_store_override_beats_franchise()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var franchise = Guid.NewGuid(); var store = Guid.NewGuid();

        // Franchise demands 500; the store lowers it to 100.
        AddSetting(raw, "franchise", brand, franchise, null,  SettingCategories.Orders, SettingKeys.MinOrderValue, "500", "decimal");
        AddSetting(raw, "store",     brand, null,      store, SettingCategories.Orders, SettingKeys.MinOrderValue, "100", "decimal");
        await raw.SaveChangesAsync();

        // Resolved at store scope ⇒ min 100 ⇒ a 200 subtotal passes.
        await MinOrderValueRule.EnforceAsync(db, brand, franchise, store, subtotal: 200m, default);

        // Without the store override (franchise scope only) the same 200 subtotal is blocked by 500.
        await Assert.ThrowsAsync<StructuredBusinessRuleException>(() =>
            MinOrderValueRule.EnforceAsync(db, brand, franchise, storeId: null, subtotal: 200m, default));
    }

    // ── Customer catalog-config query handler ────────────────────────────────────

    [Fact]
    public async Task Config_returns_nulls_and_default_currency_when_unset()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        await raw.SaveChangesAsync();

        var handler = new GetCustomerCatalogConfigHandler(db, Options.Create(new operations.Application.Orders.Common.OrdersSettings()));
        var cfg = await handler.HandleAsync(new GetCustomerCatalogConfigQuery(Guid.NewGuid(), null), default);

        Assert.Null(cfg.MinOrderValue);
        Assert.Null(cfg.HighValueGarmentThreshold);
        Assert.Equal("INR", cfg.CurrencyCode);   // falls back to OrdersSettings.DefaultCurrencyCode
    }

    [Fact]
    public async Task Config_returns_resolved_values_when_set()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        AddSetting(raw, "brand", brand, null, null, SettingCategories.Orders,  SettingKeys.MinOrderValue,             "300",     "decimal");
        AddSetting(raw, "brand", brand, null, null, SettingCategories.Orders,  SettingKeys.CurrencyCode,              "\"AED\"", "string");
        AddSetting(raw, "brand", brand, null, null, SettingCategories.Catalog, SettingKeys.HighValueGarmentThreshold, "2000",    "decimal");
        await raw.SaveChangesAsync();

        var handler = new GetCustomerCatalogConfigHandler(db, Options.Create(new operations.Application.Orders.Common.OrdersSettings()));
        var cfg = await handler.HandleAsync(new GetCustomerCatalogConfigQuery(brand, null), default);

        Assert.Equal(300m, cfg.MinOrderValue);
        Assert.Equal("AED", cfg.CurrencyCode);
        Assert.Equal(2000m, cfg.HighValueGarmentThreshold);
    }

    [Fact]
    public async Task Config_derives_franchise_from_store_for_store_scoped_override()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var franchise = Guid.NewGuid(); var store = Guid.NewGuid();
        AddStore(raw, brand, franchise, store);

        AddSetting(raw, "brand", brand, null,  null,  SettingCategories.Orders, SettingKeys.MinOrderValue, "300", "decimal");
        AddSetting(raw, "store", brand, null,  store, SettingCategories.Orders, SettingKeys.MinOrderValue, "150", "decimal");
        await raw.SaveChangesAsync();

        var handler = new GetCustomerCatalogConfigHandler(db, Options.Create(new operations.Application.Orders.Common.OrdersSettings()));
        var cfg = await handler.HandleAsync(new GetCustomerCatalogConfigQuery(brand, store), default);

        Assert.Equal(150m, cfg.MinOrderValue);   // store row wins once its franchise is derived
    }
}
