using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Catalog.Catalog.Commands.Item;
using operations.Application.Catalog.Catalog.Dtos;
using Xunit;

namespace operations.Tests.Catalog.Import;

/// <summary>
/// Handler coverage for the extended #20 import: auto-created categories, unknown-fabric row errors,
/// rejection of a published target list, row-level tax application, and redirecting writes to a draft
/// list. Runs against an in-memory operations context (no RLS / no relational constraints).
/// </summary>
public class ImportItemsHandlerTests
{
    private static Guid SeedService(LaundryGharDbContextSeed seed, string name)
    {
        var id = Guid.NewGuid();
        seed.Raw.Services.Add(new Service
        {
            Id = id, BrandId = seed.BrandId, CategoryId = Guid.NewGuid(),
            Code = name.ToUpperInvariant(), Name = name, NameLocalized = $"{{\"en\":\"{name}\"}}",
            PricingModel = "per_piece", BaseTatHours = 24, ExpressTatHours = 12, ExpressMultiplier = 1.5m,
            Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1,
        });
        return id;
    }

    private static Guid SeedFabric(LaundryGharDbContextSeed seed, string name)
    {
        var id = Guid.NewGuid();
        seed.Raw.FabricTypes.Add(new FabricType
        {
            Id = id, BrandId = seed.BrandId, Code = name.ToUpperInvariant(), Name = name,
            NameLocalized = $"{{\"en\":\"{name}\"}}", PriceMultiplier = 1m, Status = "active",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        return id;
    }

    [Fact]
    public async Task AutoCreateCategories_creates_missing_group_and_links_item()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brandId = Guid.NewGuid();
        var seed = new LaundryGharDbContextSeed(raw, brandId);
        SeedService(seed, "Wash");
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brandId);
        var handler = new ImportItemsHandler(db, user);

        var req = new ImportItemsRequest(
            [new ImportItemRow("SHIRT", "Shirt", "BrandNewCat", null, null,
                [new ImportItemServicePrice("Wash", 30m)])],
            new ImportOptions(AutoCreateCategories: true));

        var result = await handler.HandleAsync(new ImportItemsCommand(req, user.UserId), CancellationToken.None);

        Assert.Equal(1, result.CategoriesCreated);
        Assert.Equal(1, result.Created);

        var group = await raw.ItemGroups.SingleAsync(g => g.Name == "BrandNewCat");
        var item = await raw.Items.SingleAsync(i => i.Code == "SHIRT");
        Assert.Equal(group.Id, item.ItemGroupId);
    }

    [Fact]
    public async Task UnknownFabric_is_reported_and_base_price_still_set()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brandId = Guid.NewGuid();
        var seed = new LaundryGharDbContextSeed(raw, brandId);
        SeedService(seed, "Wash"); // no fabrics seeded
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brandId);
        var handler = new ImportItemsHandler(db, user);

        var req = new ImportItemsRequest(
            [new ImportItemRow("SHIRT", "Shirt", null, null, null,
                [new ImportItemServicePrice("Wash", 30m, [new ImportItemFabricPrice("Nylon", 50m)])])]);

        var result = await handler.HandleAsync(new ImportItemsCommand(req, user.UserId), CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Contains("fabric 'Nylon' not found", StringComparison.OrdinalIgnoreCase));

        var item = await raw.Items.SingleAsync(i => i.Code == "SHIRT");
        var prices = await raw.PriceListItems.Where(p => p.ItemId == item.Id).ToListAsync();
        var baseRow = Assert.Single(prices);
        Assert.Null(baseRow.FabricTypeId);
        Assert.Equal(30m, baseRow.BasePrice);
    }

    [Fact]
    public async Task PublishedTargetList_is_rejected()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brandId = Guid.NewGuid();
        var seed = new LaundryGharDbContextSeed(raw, brandId);
        SeedService(seed, "Wash");

        var publishedId = Guid.NewGuid();
        raw.PriceLists.Add(new PriceList
        {
            Id = publishedId, BrandId = brandId, Code = "PUB", Name = "Published", CurrencyCode = "INR",
            ScopeType = "brand", VersionNumber = 1, EffectiveFrom = DateTimeOffset.UtcNow,
            IsPublished = true, Status = "published",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1,
        });
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brandId);
        var handler = new ImportItemsHandler(db, user);

        var req = new ImportItemsRequest(
            [new ImportItemRow("SHIRT", "Shirt", null, null, null, [new ImportItemServicePrice("Wash", 30m)])],
            new ImportOptions(TargetPriceListId: publishedId));

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.HandleAsync(new ImportItemsCommand(req, user.UserId), CancellationToken.None));
    }

    [Fact]
    public async Task RowTax_sets_tax_rate_and_taxable_flag()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brandId = Guid.NewGuid();
        var seed = new LaundryGharDbContextSeed(raw, brandId);
        SeedService(seed, "Wash");
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brandId);
        var handler = new ImportItemsHandler(db, user);

        var req = new ImportItemsRequest(
            [new ImportItemRow("SHIRT", "Shirt", null, null, null,
                [new ImportItemServicePrice("Wash", 30m)], TaxRatePercent: 5m)]);

        await handler.HandleAsync(new ImportItemsCommand(req, user.UserId), CancellationToken.None);

        var item = await raw.Items.SingleAsync(i => i.Code == "SHIRT");
        var row = await raw.PriceListItems.SingleAsync(p => p.ItemId == item.Id && p.FabricTypeId == null);
        Assert.Equal(5m, row.TaxRatePercent);
        Assert.True(row.IsTaxable);
    }

    [Fact]
    public async Task TargetDraftList_receives_the_prices()
    {
        var (db, raw) = ImportTestSupport.NewDb();
        var brandId = Guid.NewGuid();
        var seed = new LaundryGharDbContextSeed(raw, brandId);
        SeedService(seed, "Wash");

        var draftId = Guid.NewGuid();
        raw.PriceLists.Add(new PriceList
        {
            Id = draftId, BrandId = brandId, Code = "DRAFT", Name = "Draft", CurrencyCode = "INR",
            ScopeType = "brand", VersionNumber = 2, EffectiveFrom = DateTimeOffset.UtcNow,
            IsPublished = false, Status = "draft",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Version = 1,
        });
        await raw.SaveChangesAsync();

        var user = new ImportTestSupport.FakeCurrentUser(brandId, withinScope: true);
        var handler = new ImportItemsHandler(db, user);

        var req = new ImportItemsRequest(
            [new ImportItemRow("SHIRT", "Shirt", null, null, null, [new ImportItemServicePrice("Wash", 42m)])],
            new ImportOptions(TargetPriceListId: draftId));

        await handler.HandleAsync(new ImportItemsCommand(req, user.UserId), CancellationToken.None);

        var item = await raw.Items.SingleAsync(i => i.Code == "SHIRT");
        var row = await raw.PriceListItems.SingleAsync(p => p.ItemId == item.Id);
        Assert.Equal(draftId, row.PriceListId);
        Assert.Equal(42m, row.BasePrice);
    }
}

/// <summary>Tiny seed context holder so the per-test helpers can share brand + raw context.</summary>
internal sealed class LaundryGharDbContextSeed(LaundryGharDbContext raw, Guid brandId)
{
    public LaundryGharDbContext Raw { get; } = raw;
    public Guid BrandId { get; } = brandId;
}
