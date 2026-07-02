using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using operations.Application.Catalog.Catalog.Commands.Item;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Pricing.Commands.ValueSlab;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Catalog.Pricing.Dtos;
using operations.Application.Catalog.Pricing.Queries.ValueSlab;
using operations.Application.Common.Interfaces;
using operations.Application.Orders.Common;
using operations.Tests.Catalog.Import;
using Xunit;

namespace operations.Tests.Catalog;

/// <summary>
/// Coverage for GH #22 value-slab pricing: <see cref="ValueSlabResolver"/> boundary resolution and
/// lane precedence, the declared-value / no-match structured errors, slab CRUD with change-log +
/// overlap validation, the order-path <see cref="PriceResolver"/> slab branch, and the item
/// pricing-mode update. Runs against the in-memory operations context (Settings/Import pattern).
/// </summary>
public class ValueSlabTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static (Item item, Service svc) SeedItemService(
        LaundryGharDbContext raw, Guid brand, string pricingMode)
    {
        var svc = new Service
        {
            Id = Guid.NewGuid(), BrandId = brand, CategoryId = Guid.NewGuid(),
            Code = "DC", Name = "Dry Clean", NameLocalized = "{}", PricingModel = "per_item",
            Status = "active", CreatedAt = Now, UpdatedAt = Now, Version = 1,
        };
        var item = new Item
        {
            Id = Guid.NewGuid(), BrandId = brand, Code = "BLZ", Name = "Blazer",
            NameLocalized = "{}", Attributes = "{}", CatalogKind = "laundry_garment",
            PricingMode = pricingMode, Aliases = [], Status = "active", DisplayOrder = 100,
            CreatedAt = Now, UpdatedAt = Now, Version = 1,
        };
        raw.Services.Add(svc);
        raw.Items.Add(item);
        return (item, svc);
    }

    private static void AddSlab(
        LaundryGharDbContext raw, Guid brand, Guid? serviceId,
        decimal min, decimal? max, decimal price, string status = "active")
    {
        raw.ValuePriceSlabs.Add(new ValuePriceSlab
        {
            Id = Guid.NewGuid(), BrandId = brand, ServiceId = serviceId,
            MinValue = min, MaxValue = max, Price = price, Status = status,
            CreatedAt = Now, UpdatedAt = Now, Version = 1,
        });
    }

    // ── Resolution boundaries ─────────────────────────────────────────────────

    [Fact]
    public async Task Slab_min_is_inclusive_and_max_is_exclusive()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var svc = Guid.NewGuid(); var item = Guid.NewGuid();
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);
        AddSlab(raw, brand, null, 5000m, 10000m, 300m);
        await raw.SaveChangesAsync();

        // At min (2000) → lower slab; just under max (4999.99) → lower slab.
        Assert.Equal(200m, await ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 2000m, item, default));
        Assert.Equal(200m, await ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 4999.99m, item, default));
        // At the shared boundary (5000) → upper slab (max exclusive on the lower one).
        Assert.Equal(300m, await ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 5000m, item, default));
    }

    [Fact]
    public async Task Open_ended_top_slab_matches_any_value_at_or_above_min()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var svc = Guid.NewGuid(); var item = Guid.NewGuid();
        AddSlab(raw, brand, null, 90000m, null, 2200m);
        await raw.SaveChangesAsync();

        Assert.Equal(2200m, await ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 90000m, item, default));
        Assert.Equal(2200m, await ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 500000m, item, default));
    }

    [Fact]
    public async Task Service_specific_slab_beats_brand_wide_lane()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var svc = Guid.NewGuid(); var item = Guid.NewGuid();
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);   // brand-wide lane
        AddSlab(raw, brand, svc,  2000m, 5000m, 500m);   // service-specific lane
        await raw.SaveChangesAsync();

        Assert.Equal(500m, await ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 3000m, item, default));
    }

    [Fact]
    public async Task Falls_back_to_brand_wide_lane_when_no_service_specific_match()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var svc = Guid.NewGuid(); var item = Guid.NewGuid();
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);      // brand-wide covers 3000
        AddSlab(raw, brand, svc,  10000m, 20000m, 900m);    // service-specific does NOT cover 3000
        await raw.SaveChangesAsync();

        Assert.Equal(200m, await ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 3000m, item, default));
    }

    [Fact]
    public async Task No_match_throws_structured_error()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var svc = Guid.NewGuid(); var item = Guid.NewGuid();
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);
        await raw.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<StructuredBusinessRuleException>(() =>
            ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 6000m, item, default));
        Assert.Equal(ValueSlabResolver.NoSlabMatchCode, ex.Code);
        Assert.Equal("6000", ex.Fields["declaredValue"]);
    }

    [Fact]
    public void Declared_value_required_when_missing_or_nonpositive()
    {
        var item = Guid.NewGuid();
        foreach (var bad in new decimal?[] { null, 0m, -5m })
        {
            var ex = Assert.Throws<StructuredBusinessRuleException>(() =>
                ValueSlabResolver.RequireDeclaredValue(bad, item, "Blazer"));
            Assert.Equal(ValueSlabResolver.DeclaredValueRequiredCode, ex.Code);
            Assert.Equal(item.ToString(), ex.Fields["itemId"]);
        }
        // A positive value passes (no throw).
        ValueSlabResolver.RequireDeclaredValue(1m, item, "Blazer");
    }

    // ── Order-path PriceResolver slab branch ──────────────────────────────────

    [Fact]
    public async Task PriceResolver_prices_value_slab_item_from_declared_value()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var (item, svc) = SeedItemService(raw, brand, "value_slab");
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);
        await raw.SaveChangesAsync();

        var resolved = await PriceResolver.ResolveAsync(
            db, brand, storeId: Guid.NewGuid(), svc.Id, item.Id, variantId: null, default, declaredValue: 3000m);

        Assert.NotNull(resolved);
        Assert.True(resolved!.IsValueSlab);
        Assert.Null(resolved.PriceListItemId);
        Assert.Equal(200m, resolved.BasePrice);
        Assert.Null(resolved.ExpressPrice);            // express surcharge applies downstream, not here
        Assert.Equal("Blazer", resolved.ItemNameSnapshot);
    }

    [Fact]
    public async Task PriceResolver_requires_declared_value_for_slab_item()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var (item, svc) = SeedItemService(raw, brand, "value_slab");
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);
        await raw.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<StructuredBusinessRuleException>(() =>
            PriceResolver.ResolveAsync(db, brand, Guid.NewGuid(), svc.Id, item.Id, null, default, declaredValue: null));
        Assert.Equal(ValueSlabResolver.DeclaredValueRequiredCode, ex.Code);
    }

    // ── CRUD + change log + overlap ───────────────────────────────────────────

    private static ICurrentUser User(Guid brand) => new ImportTestSupport.FakeCurrentUser(brand);

    [Fact]
    public async Task Create_persists_slab_and_logs_change()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        await raw.SaveChangesAsync();

        var dto = await new CreateValueSlabHandler(db, User(brand)).HandleAsync(
            new CreateValueSlabCommand(new CreateValueSlabRequest(null, 2000m, 5000m, 200m), Guid.NewGuid()), default);

        Assert.Equal(200m, dto.Price);
        Assert.Single(raw.ValuePriceSlabs);
        var log = Assert.Single(raw.PricingChangeLogs);
        Assert.Equal("value_price_slab", log.TargetKind);
        Assert.Equal(dto.Id, log.TargetId);
    }

    [Fact]
    public async Task Create_rejects_overlapping_range_in_same_lane()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);
        await raw.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<StructuredBusinessRuleException>(() =>
            new CreateValueSlabHandler(db, User(brand)).HandleAsync(
                new CreateValueSlabCommand(new CreateValueSlabRequest(null, 4000m, 6000m, 300m), Guid.NewGuid()), default));
        Assert.Equal(ValueSlabResolver.OverlapCode, ex.Code);
    }

    [Fact]
    public async Task Create_allows_same_range_in_separate_lanes()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var (_, svc) = SeedItemService(raw, brand, "standard");
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);   // brand-wide lane
        await raw.SaveChangesAsync();

        // Same 2000–5000 range in the service-specific lane is allowed (lanes are independent).
        var dto = await new CreateValueSlabHandler(db, User(brand)).HandleAsync(
            new CreateValueSlabCommand(new CreateValueSlabRequest(svc.Id, 2000m, 5000m, 500m), Guid.NewGuid()), default);
        Assert.Equal(svc.Id, dto.ServiceId);
    }

    [Fact]
    public async Task Update_changes_price_and_logs_second_entry()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var created = await new CreateValueSlabHandler(db, User(brand)).HandleAsync(
            new CreateValueSlabCommand(new CreateValueSlabRequest(null, 2000m, 5000m, 200m), Guid.NewGuid()), default);

        var updated = await new UpdateValueSlabHandler(db, User(brand)).HandleAsync(
            new UpdateValueSlabCommand(created.Id, new UpdateValueSlabRequest(null, 2000m, 5000m, 250m, "active"), Guid.NewGuid()), default);

        Assert.NotNull(updated);
        Assert.Equal(250m, updated!.Price);
        Assert.Equal(2, raw.PricingChangeLogs.Count());   // create + update
    }

    [Fact]
    public async Task Delete_archives_slab_and_drops_it_from_resolution()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid(); var svc = Guid.NewGuid(); var item = Guid.NewGuid();
        var created = await new CreateValueSlabHandler(db, User(brand)).HandleAsync(
            new CreateValueSlabCommand(new CreateValueSlabRequest(null, 2000m, 5000m, 200m), Guid.NewGuid()), default);

        var ok = await new DeleteValueSlabHandler(db, User(brand)).HandleAsync(
            new DeleteValueSlabCommand(created.Id, Guid.NewGuid()), default);
        Assert.True(ok);

        // Archived slab no longer resolves.
        await Assert.ThrowsAsync<StructuredBusinessRuleException>(() =>
            ValueSlabResolver.ResolveSlabPriceAsync(db, brand, svc, 3000m, item, default));
    }

    [Fact]
    public async Task List_returns_active_slabs_excluding_archived_by_default()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        AddSlab(raw, brand, null, 2000m, 5000m, 200m);
        AddSlab(raw, brand, null, 5000m, 10000m, 300m, status: "archived");
        await raw.SaveChangesAsync();

        var active = await new GetValueSlabsHandler(db, User(brand)).HandleAsync(
            new GetValueSlabsQuery(null, IncludeArchived: false), default);
        Assert.Single(active);

        var all = await new GetValueSlabsHandler(db, User(brand)).HandleAsync(
            new GetValueSlabsQuery(null, IncludeArchived: true), default);
        Assert.Equal(2, all.Count);
    }

    // ── Item pricing-mode update ──────────────────────────────────────────────

    [Fact]
    public async Task Item_update_can_flip_pricing_mode_to_value_slab()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var (item, _) = SeedItemService(raw, brand, "standard");
        await raw.SaveChangesAsync();

        var req = new UpdateItemRequest(
            ItemGroupId: null, Name: "Blazer", NameLocalized: "{}", Description: null,
            IconUrl: null, ImageUrl: null, TypicalWeightGrams: null, RequiresPerSidePrice: false,
            Aliases: [], DisplayOrder: 100, Status: "active", PricingMode: "value_slab");

        var dto = await new UpdateItemHandler(db, User(brand)).HandleAsync(
            new UpdateItemCommand(item.Id, req, Guid.NewGuid()), default);

        Assert.NotNull(dto);
        Assert.Equal("value_slab", dto!.PricingMode);
    }

    [Fact]
    public async Task Item_update_rejects_invalid_pricing_mode()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var (item, _) = SeedItemService(raw, brand, "standard");
        await raw.SaveChangesAsync();

        var req = new UpdateItemRequest(
            ItemGroupId: null, Name: "Blazer", NameLocalized: "{}", Description: null,
            IconUrl: null, ImageUrl: null, TypicalWeightGrams: null, RequiresPerSidePrice: false,
            Aliases: [], DisplayOrder: 100, Status: "active", PricingMode: "bogus");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new UpdateItemHandler(db, User(brand)).HandleAsync(
                new UpdateItemCommand(item.Id, req, Guid.NewGuid()), default));
    }
}
