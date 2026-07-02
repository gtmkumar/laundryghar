using System.Text;
using System.Text.Json;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.Item;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Catalog.Import;
using operations.Application.Catalog.Pricing.Commands.Revert;
using operations.Application.Catalog.Pricing.Common;
using operations.Application.Catalog.Pricing.Queries.PriceLists;
using operations.Tests.Catalog.Import;
using Xunit;

namespace operations.Tests.Catalog;

/// <summary>
/// GH #24 backend polish: catalog item change audit (create/update/delete logged, UPDATE revertible,
/// create/delete not), SKU/code edit uniqueness (item_code_taken), and the price-list export that
/// round-trips through <see cref="ImportFileParser"/>. In-memory operations context (Import pattern).
/// </summary>
public class ItemAuditAndExportTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Item SeedItem(LaundryGharDbContext raw, Guid brand, string code, string name = "Shirt")
    {
        var item = new Item
        {
            Id = Guid.NewGuid(), BrandId = brand, Code = code, Name = name,
            NameLocalized = "{}", Attributes = "{}", CatalogKind = "laundry_garment",
            PricingMode = "standard", Aliases = [], Status = "active", DisplayOrder = 1,
            TatHours = 24, CreatedAt = Now, UpdatedAt = Now, Version = 1,
        };
        raw.Items.Add(item);
        return item;
    }

    private static UpdateItemRequest UpdateReq(string name, string status = "active",
        string? pricingMode = null, string? code = null) =>
        new(ItemGroupId: null, Name: name, NameLocalized: "{}", Description: null,
            IconUrl: null, ImageUrl: null, TypicalWeightGrams: null, RequiresPerSidePrice: false,
            Aliases: [], DisplayOrder: 1, Status: status, PricingMode: pricingMode, Code: code);

    // ── Item audit ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_item_writes_item_audit_entry()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        var dto = await new CreateItemHandler(db, user).HandleAsync(
            new CreateItemCommand(new CreateItemRequest(
                ItemGroupId: null, Code: "SHIRT", Name: "Shirt", NameLocalized: "{}",
                Description: null, IconUrl: null, ImageUrl: null, TypicalWeightGrams: null,
                RequiresPerSidePrice: false, Aliases: null, DisplayOrder: 1), user.UserId), default);

        var log = Assert.Single(raw.PricingChangeLogs);
        Assert.Equal("item", log.TargetKind);
        Assert.Equal(dto.Id, log.TargetId);
        var before = JsonSerializer.Deserialize<ItemAuditEnvelope>(log.BeforeJson!);
        Assert.Equal(ItemAudit.OpCreate, before!.Op);
        Assert.Null(before.State); // create carries no prior state
    }

    [Fact]
    public async Task Update_item_writes_before_and_after_snapshot()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var item = SeedItem(raw, brand, "SHIRT", "Shirt");
        await raw.SaveChangesAsync();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        await new UpdateItemHandler(db, user).HandleAsync(
            new UpdateItemCommand(item.Id, UpdateReq("Formal Shirt"), user.UserId), default);

        var log = Assert.Single(raw.PricingChangeLogs.Where(x => x.TargetKind == "item"));
        var before = JsonSerializer.Deserialize<ItemAuditEnvelope>(log.BeforeJson!);
        var after = JsonSerializer.Deserialize<ItemAuditEnvelope>(log.AfterJson!);
        Assert.Equal(ItemAudit.OpUpdate, before!.Op);
        Assert.Equal("Shirt", before.State!.Name);
        Assert.Equal("Formal Shirt", after!.State!.Name);
    }

    [Fact]
    public async Task Delete_item_writes_delete_audit_entry()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var item = SeedItem(raw, brand, "SHIRT");
        await raw.SaveChangesAsync();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        await new DeleteItemHandler(db, user).HandleAsync(new DeleteItemCommand(item.Id, user.UserId), default);

        var log = Assert.Single(raw.PricingChangeLogs.Where(x => x.TargetKind == "item"));
        var before = JsonSerializer.Deserialize<ItemAuditEnvelope>(log.BeforeJson!);
        var after = JsonSerializer.Deserialize<ItemAuditEnvelope>(log.AfterJson!);
        Assert.Equal(ItemAudit.OpDelete, before!.Op);
        Assert.NotNull(before.State);
        Assert.Null(after!.State); // delete carries no after-state
    }

    [Fact]
    public async Task Revert_item_update_restores_prior_fields()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var item = SeedItem(raw, brand, "SHIRT", "Shirt");
        await raw.SaveChangesAsync();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        await new UpdateItemHandler(db, user).HandleAsync(
            new UpdateItemCommand(item.Id, UpdateReq("Formal Shirt", status: "disabled"), user.UserId), default);

        var updateLog = await raw.PricingChangeLogs.SingleAsync(x => x.TargetKind == "item");
        var ok = await new RevertPricingChangeHandler(db, user).HandleAsync(
            new RevertPricingChangeCommand(updateLog.Id, user.UserId), default);

        Assert.True(ok);
        var reloaded = await raw.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal("Shirt", reloaded.Name);       // name restored
        Assert.Equal("active", reloaded.Status);      // status restored
        var log = await raw.PricingChangeLogs.SingleAsync(x => x.Id == updateLog.Id);
        Assert.NotNull(log.RevertedAt);
    }

    [Fact]
    public async Task Revert_item_create_is_rejected()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        var dto = await new CreateItemHandler(db, user).HandleAsync(
            new CreateItemCommand(new CreateItemRequest(
                null, "SHIRT", "Shirt", "{}", null, null, null, null, false, null, 1), user.UserId), default);

        var createLog = await raw.PricingChangeLogs.SingleAsync(x => x.TargetKind == "item" && x.TargetId == dto.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new RevertPricingChangeHandler(db, user).HandleAsync(
                new RevertPricingChangeCommand(createLog.Id, user.UserId), default));
    }

    [Fact]
    public async Task Revert_item_delete_is_rejected()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var item = SeedItem(raw, brand, "SHIRT");
        await raw.SaveChangesAsync();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        await new DeleteItemHandler(db, user).HandleAsync(new DeleteItemCommand(item.Id, user.UserId), default);
        var deleteLog = await raw.PricingChangeLogs.SingleAsync(x => x.TargetKind == "item");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new RevertPricingChangeHandler(db, user).HandleAsync(
                new RevertPricingChangeCommand(deleteLog.Id, user.UserId), default));
    }

    // ── SKU / code edit ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Code_edit_to_a_taken_code_throws_item_code_taken()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        SeedItem(raw, brand, "SHIRT", "Shirt");
        var target = SeedItem(raw, brand, "TROUSER", "Trouser");
        await raw.SaveChangesAsync();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        var ex = await Assert.ThrowsAsync<StructuredBusinessRuleException>(() =>
            new UpdateItemHandler(db, user).HandleAsync(
                new UpdateItemCommand(target.Id, UpdateReq("Trouser", code: "SHIRT"), user.UserId), default));

        Assert.Equal("item_code_taken", ex.Code);
        Assert.Equal("SHIRT", ex.Fields["code"]);
    }

    [Fact]
    public async Task Code_edit_to_a_free_code_succeeds_and_is_audited()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var item = SeedItem(raw, brand, "SHIRT", "Shirt");
        await raw.SaveChangesAsync();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        var dto = await new UpdateItemHandler(db, user).HandleAsync(
            new UpdateItemCommand(item.Id, UpdateReq("Shirt", code: "SHIRT-V2"), user.UserId), default);

        Assert.Equal("SHIRT-V2", dto!.Code);
        var log = await raw.PricingChangeLogs.SingleAsync(x => x.TargetKind == "item");
        var before = JsonSerializer.Deserialize<ItemAuditEnvelope>(log.BeforeJson!);
        var after = JsonSerializer.Deserialize<ItemAuditEnvelope>(log.AfterJson!);
        Assert.Equal("SHIRT", before!.State!.Code);
        Assert.Equal("SHIRT-V2", after!.State!.Code);
    }

    [Fact]
    public async Task Code_edit_reusing_a_deleted_items_code_is_allowed()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var deleted = SeedItem(raw, brand, "OLD", "Old");
        deleted.DeletedAt = Now; deleted.Status = "disabled";
        var item = SeedItem(raw, brand, "SHIRT", "Shirt");
        await raw.SaveChangesAsync();
        var user = new ImportTestSupport.FakeCurrentUser(brand);

        var dto = await new UpdateItemHandler(db, user).HandleAsync(
            new UpdateItemCommand(item.Id, UpdateReq("Shirt", code: "OLD"), user.UserId), default);

        Assert.Equal("OLD", dto!.Code); // deleted items don't reserve the code
    }

    // ── Price-list export round-trip ─────────────────────────────────────────────

    [Fact]
    public async Task Export_csv_round_trips_through_the_import_parser()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();

        var group = new ItemGroup
        {
            Id = Guid.NewGuid(), BrandId = brand, Code = "MENS", Name = "Mens",
            NameLocalized = "{}", Status = "active", CreatedAt = Now, UpdatedAt = Now,
        };
        var wash = new Service
        {
            Id = Guid.NewGuid(), BrandId = brand, CategoryId = Guid.NewGuid(), Code = "WASH",
            Name = "Wash", NameLocalized = "{}", PricingModel = "per_item", DisplayOrder = 1,
            Status = "active", CreatedAt = Now, UpdatedAt = Now, Version = 1,
        };
        var dryClean = new Service
        {
            Id = Guid.NewGuid(), BrandId = brand, CategoryId = Guid.NewGuid(), Code = "DC",
            Name = "Dry Clean", NameLocalized = "{}", PricingModel = "per_item", DisplayOrder = 2,
            Status = "active", CreatedAt = Now, UpdatedAt = Now, Version = 1,
        };
        var silk = new FabricType
        {
            Id = Guid.NewGuid(), BrandId = brand, Code = "SILK", Name = "Silk",
            NameLocalized = "{}", PriceMultiplier = 1m, Status = "active", CreatedAt = Now, UpdatedAt = Now,
        };
        var item = SeedItem(raw, brand, "SHIRT", "Shirt");
        item.ItemGroupId = group.Id;

        var list = new PriceList
        {
            Id = Guid.NewGuid(), BrandId = brand, Code = "BRANDLIST", Name = "Brand List", CurrencyCode = "INR",
            ScopeType = "brand", VersionNumber = 1, EffectiveFrom = Now, IsPublished = true, Status = "published",
            CreatedAt = Now, UpdatedAt = Now, Version = 1,
        };
        raw.ItemGroups.Add(group);
        raw.Services.AddRange(wash, dryClean);
        raw.FabricTypes.Add(silk);
        raw.PriceLists.Add(list);

        void AddRow(Guid serviceId, Guid? fabricId, decimal price) => raw.PriceListItems.Add(new PriceListItem
        {
            Id = Guid.NewGuid(), PriceListId = list.Id, BrandId = brand, ServiceId = serviceId, ItemId = item.Id,
            FabricTypeId = fabricId, BasePrice = price, MinimumQuantity = 1, TaxRatePercent = 5m, IsTaxable = true,
            IsActive = true, Status = "active", CreatedAt = Now, UpdatedAt = Now,
        });
        AddRow(wash.Id, null, 30m);
        AddRow(dryClean.Id, null, 50m);
        AddRow(dryClean.Id, silk.Id, 80m);
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brand);
        var file = await new ExportPriceListHandler(db, user).HandleAsync(
            new ExportPriceListQuery(list.Id, "csv"), default);

        Assert.NotNull(file);
        Assert.Equal("text/csv", file!.ContentType);
        Assert.Equal("price-list-BRANDLIST.csv", file.FileName);

        // Parse the exported CSV straight back through the import parser and assert equivalence.
        using var ms = new MemoryStream(file.Content);
        var parsed = ImportFileParser.Parse(ms, "price-list-BRANDLIST.csv");
        Assert.Empty(parsed.Errors);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("SHIRT", row.Code);
        Assert.Equal("Shirt", row.Name);
        Assert.Equal("Mens", row.Category);
        Assert.Equal("active", row.Status);
        Assert.Equal(24, row.TatHours);
        Assert.Equal(5m, row.TaxRatePercent);

        var washPrice = Assert.Single(row.ServicePrices, s => s.ServiceName == "Wash");
        Assert.Equal(30m, washPrice.BasePrice);

        var dcPrice = Assert.Single(row.ServicePrices, s => s.ServiceName == "Dry Clean");
        Assert.Equal(50m, dcPrice.BasePrice);
        var silkPrice = Assert.Single(dcPrice.FabricPrices!);
        Assert.Equal("Silk", silkPrice.FabricName);
        Assert.Equal(80m, silkPrice.Price);
    }

    [Fact]
    public async Task Export_returns_null_for_unknown_list()
    {
        var (db, _) = ImportTestSupport.NewDb();
        var user = new ImportTestSupport.FakeCurrentUser(Guid.NewGuid());
        var file = await new ExportPriceListHandler(db, user).HandleAsync(
            new ExportPriceListQuery(Guid.NewGuid(), "csv"), default);
        Assert.Null(file);
    }
}

/// <summary>GH #24 pickup declared value + GH #24 resolver dedupe parity — lightweight coverage.</summary>
public class PickupDeclaredValueAndResolverParityTests
{
    [Fact]
    public void Requested_cart_item_round_trips_declared_value_through_pickup_json()
    {
        var line = new operations.Application.Orders.Pickup.Dtos.RequestedCartItemDto(
            ServiceId: Guid.NewGuid(), ItemId: Guid.NewGuid(), DisplayLabel: "Blazer – Dry Clean",
            Quantity: 1, EstimatedUnitPrice: 200m, DeclaredValue: 5000m);

        var json = JsonSerializer.Serialize(new[] { line });
        var back = JsonSerializer.Deserialize<operations.Application.Orders.Pickup.Dtos.RequestedCartItemDto[]>(json)!;

        Assert.Equal(5000m, back[0].DeclaredValue);
    }

    [Fact]
    public void Requested_cart_item_declared_value_defaults_null_for_legacy_payloads()
    {
        // A booking payload written before #24 has no declaredValue key — it must deserialize as null.
        const string legacy =
            """[{"serviceId":null,"itemId":null,"displayLabel":"Shirt","quantity":2,"estimatedUnitPrice":49}]""";
        var back = JsonSerializer.Deserialize<operations.Application.Orders.Pickup.Dtos.RequestedCartItemDto[]>(legacy)!;
        Assert.Null(back[0].DeclaredValue);
    }

    [Fact]
    public async Task Order_resolver_and_admin_resolver_agree_on_a_standard_price()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brand = Guid.NewGuid();
        var svc = new Service
        {
            Id = Guid.NewGuid(), BrandId = brand, CategoryId = Guid.NewGuid(), Code = "WASH",
            Name = "Wash", NameLocalized = "{}", PricingModel = "per_item", Status = "active",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1,
        };
        var item = new Item
        {
            Id = Guid.NewGuid(), BrandId = brand, Code = "SHIRT", Name = "Shirt", NameLocalized = "{}",
            Attributes = "{}", CatalogKind = "laundry_garment", PricingMode = "standard", Aliases = [],
            Status = "active", DisplayOrder = 1, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1,
        };
        var list = new PriceList
        {
            Id = Guid.NewGuid(), BrandId = brand, Code = "BRAND", Name = "Brand", CurrencyCode = "INR",
            ScopeType = "brand", VersionNumber = 1, EffectiveFrom = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow, IsPublished = true, Status = "published",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1,
        };
        raw.Services.Add(svc); raw.Items.Add(item); raw.PriceLists.Add(list);
        raw.PriceListItems.Add(new PriceListItem
        {
            Id = Guid.NewGuid(), PriceListId = list.Id, BrandId = brand, ServiceId = svc.Id, ItemId = item.Id,
            BasePrice = 42m, MinimumQuantity = 1, TaxRatePercent = 0m, IsTaxable = false, IsActive = true,
            Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brand);
        var orderPrice = await operations.Application.Orders.Common.PriceResolver.ResolveAsync(
            db, brand, storeId: Guid.NewGuid(), svc.Id, item.Id, variantId: null, default);
        var adminPrice = await new operations.Application.Catalog.Pricing.Queries.PriceResolution.ResolvePriceHandler(db, user)
            .HandleAsync(new operations.Application.Catalog.Pricing.Queries.PriceResolution.ResolvePriceQuery(
                item.Id, svc.Id, null, null), default);

        Assert.NotNull(orderPrice);
        Assert.NotNull(adminPrice);
        Assert.Equal(42m, orderPrice!.BasePrice);
        Assert.Equal(orderPrice.BasePrice, adminPrice!.BasePrice);
        Assert.Equal(list.Id, adminPrice.PriceListId);
    }
}
