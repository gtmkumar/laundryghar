using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using operations.Application.Catalog.Catalog.Commands.Item;
using operations.Application.Catalog.Catalog.Dtos;
using operations.Application.Catalog.Pricing.Queries.Matrix;
using operations.Tests.Catalog.Import;
using Xunit;

namespace operations.Tests.Catalog;

/// <summary>
/// GH #24 follow-up: the price matrix exposes item/service ids and an Editable flag so the
/// admin UI can write single cells through SaveItemPricing without touching the wrong list,
/// and a ServicePrices-only save must leave the item's fabric variants alone.
/// </summary>
public sealed class PricingMatrixEditTests
{
    private static readonly Guid BrandId = Guid.NewGuid();

    private static async Task<(Guid itemId, Guid serviceId, Guid workingListId, Guid fabricId)> SeedAsync(
        operations.Application.Common.Interfaces.IOperationsDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var service = new Service
        {
            Id = Guid.NewGuid(), BrandId = BrandId, CategoryId = Guid.NewGuid(), Code = "DC",
            Name = "Dry Clean", NameLocalized = "{}", PricingModel = "per_item", Status = "active",
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        var item = new Item
        {
            Id = Guid.NewGuid(), BrandId = BrandId, Code = "SHIRT", Name = "Shirt",
            NameLocalized = "{}", Attributes = "{}", CatalogKind = "laundry_garment",
            Aliases = [], Status = "active", CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        var fabric = new FabricType
        {
            Id = Guid.NewGuid(), BrandId = BrandId, Code = "SILK", Name = "Silk",
            NameLocalized = "{}", PriceMultiplier = 1.3m, Status = "active",
            CreatedAt = now, UpdatedAt = now,
        };
        var working = new PriceList
        {
            Id = Guid.NewGuid(), BrandId = BrandId, Code = "MAIN-WORKING", Name = "Working",
            CurrencyCode = "INR", ScopeType = "brand", VersionNumber = 1, EffectiveFrom = now,
            IsDefault = true, IsPublished = true, PublishedAt = now, Status = "published",
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        var baseRow = new PriceListItem
        {
            Id = Guid.NewGuid(), PriceListId = working.Id, BrandId = BrandId,
            ServiceId = service.Id, ItemId = item.Id, BasePrice = 170m, MinimumQuantity = 1,
            DisplayLabel = "Shirt – Dry Clean", IsActive = true, Status = "active",
            CreatedAt = now, UpdatedAt = now,
        };
        var fabricRow = new PriceListItem
        {
            Id = Guid.NewGuid(), PriceListId = working.Id, BrandId = BrandId,
            ServiceId = service.Id, ItemId = item.Id, FabricTypeId = fabric.Id,
            BasePrice = 221m, MinimumQuantity = 1, DisplayLabel = "Shirt – Dry Clean (Silk)",
            IsActive = true, Status = "active", CreatedAt = now.AddSeconds(1), UpdatedAt = now,
        };
        var variant = new ItemVariant
        {
            Id = Guid.NewGuid(), BrandId = BrandId, ItemId = item.Id, FabricTypeId = fabric.Id,
            Code = "SHIRT-SILK", VariantName = "Silk", Status = "active",
            CreatedAt = now, UpdatedAt = now,
        };

        db.Services.Add(service);
        db.Items.Add(item);
        db.FabricTypes.Add(fabric);
        db.PriceLists.Add(working);
        db.PriceListItems.AddRange(baseRow, fabricRow);
        db.ItemVariants.Add(variant);
        await db.SaveChangesAsync(CancellationToken.None);
        return (item.Id, service.Id, working.Id, fabric.Id);
    }

    [Fact]
    public async Task Matrix_marks_working_list_base_rows_editable_and_fabric_rows_not()
    {
        var (db, _) = ImportTestSupport.NewDb();
        var (itemId, serviceId, workingListId, _) = await SeedAsync(db);

        var handler = new GetPricingMatrixHandler(db, new ImportTestSupport.FakeCurrentUser(BrandId));
        var dto = await handler.HandleAsync(new GetPricingMatrixQuery(null), CancellationToken.None);

        Assert.True(dto.IsWorkingList);
        Assert.Equal(workingListId, dto.PriceListId);
        var baseRow = Assert.Single(dto.Rows, r => r.Editable);
        Assert.Equal(itemId, baseRow.ItemId);
        Assert.Equal(serviceId, baseRow.ServiceId);
        Assert.Equal(170m, baseRow.BasePrice);
        Assert.Single(dto.Rows, r => !r.Editable); // the fabric-specific row
    }

    [Fact]
    public async Task Matrix_from_store_override_list_is_not_editable()
    {
        var (db, _) = ImportTestSupport.NewDb();
        var (itemId, serviceId, _, _) = await SeedAsync(db);

        var now = DateTimeOffset.UtcNow;
        var store = new laundryghar.SharedDataModel.Entities.TenancyOrg.Store
        {
            Id = Guid.NewGuid(), BrandId = BrandId, FranchiseId = Guid.NewGuid(),
            Code = "S1", Name = "Sector 45", StoreType = "franchise_store",
            AddressLine1 = "1 Test Rd", City = "Gurugram", State = "HR", Pincode = "122001",
            CountryCode = "IN", Timezone = "Asia/Kolkata", CurrencyCode = "INR",
            Config = "{}", Status = "active", CreatedAt = now, UpdatedAt = now,
        };
        var storeList = new PriceList
        {
            Id = Guid.NewGuid(), BrandId = BrandId, StoreId = store.Id, Code = "S1-PL",
            Name = "Sector 45 overrides", CurrencyCode = "INR", ScopeType = "store",
            VersionNumber = 1, EffectiveFrom = now, IsPublished = true, PublishedAt = now,
            Status = "published", CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        db.Stores.Add(store);
        db.PriceLists.Add(storeList);
        db.PriceListItems.Add(new PriceListItem
        {
            Id = Guid.NewGuid(), PriceListId = storeList.Id, BrandId = BrandId,
            ServiceId = serviceId, ItemId = itemId, BasePrice = 160m, MinimumQuantity = 1,
            IsActive = true, Status = "active", CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPricingMatrixHandler(db, new ImportTestSupport.FakeCurrentUser(BrandId));
        var dto = await handler.HandleAsync(new GetPricingMatrixQuery(store.Id), CancellationToken.None);

        Assert.False(dto.IsWorkingList);
        Assert.Equal("store", dto.ScopeType);
        Assert.All(dto.Rows, r => Assert.False(r.Editable));
    }

    [Fact]
    public async Task SaveItemPricing_without_fabric_list_leaves_fabric_variants_unchanged()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var (itemId, serviceId, _, fabricId) = await SeedAsync(db);

        var handler = new SaveItemPricingHandler(db, new ImportTestSupport.FakeCurrentUser(BrandId));
        var ok = await handler.HandleAsync(new SaveItemPricingCommand(
            itemId,
            new SaveItemPricingRequest([new SaveItemServicePrice(serviceId, 185m)]),
            null), CancellationToken.None);

        Assert.True(ok);
        var variant = Assert.Single(raw.ItemVariants.Where(v => v.ItemId == itemId && v.FabricTypeId == fabricId));
        Assert.Null(variant.DeletedAt); // fabric set untouched by a prices-only save
        var row = Assert.Single(raw.PriceListItems.Where(p => p.ItemId == itemId && p.FabricTypeId == null));
        Assert.Equal(185m, row.BasePrice);
    }

    [Fact]
    public async Task SaveItemPricing_with_empty_fabric_list_still_clears_fabric_set()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var (itemId, serviceId, _, fabricId) = await SeedAsync(db);

        var handler = new SaveItemPricingHandler(db, new ImportTestSupport.FakeCurrentUser(BrandId));
        var ok = await handler.HandleAsync(new SaveItemPricingCommand(
            itemId,
            new SaveItemPricingRequest([new SaveItemServicePrice(serviceId, 185m)], []),
            null), CancellationToken.None);

        Assert.True(ok);
        var variant = Assert.Single(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .IgnoreQueryFilters(raw.ItemVariants)
            .Where(v => v.ItemId == itemId && v.FabricTypeId == fabricId));
        Assert.NotNull(variant.DeletedAt); // explicit empty list = replace with nothing
    }
}
