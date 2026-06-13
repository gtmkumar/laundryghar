using laundryghar.Catalog.Infrastructure.Auth;
using laundryghar.Catalog.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace laundryghar.Catalog.Infrastructure.Seeders;

/// <summary>
/// Idempotent catalog seeder — Development only.
/// Seeds minimal catalog data under the LG-MAIN brand so customer reads return data:
///   3 service_categories (Dry Clean / Laundry / Steam Iron)
///   3 services (one per category)
///   3 fabric_types (Cotton / Silk / Woolen)
///   2 item_groups (MEN / WOMEN)
///   3 items (Shirt / Trouser / Saree)
///   2 item_variants
///   1 add_on (Stain Treatment)
///   1 PUBLISHED brand-scoped price_list + 4 price_list_items
///
/// Natural key for check-before-insert: (brand_id, code) on every entity.
/// Resolves brand by code "LG-MAIN"; logs and skips if brand absent.
/// </summary>
public sealed class CatalogSeeder
{
    private readonly LaundryGharDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<CatalogSeeder> _logger;

    public CatalogSeeder(LaundryGharDbContext db, IHostEnvironment env, ILogger<CatalogSeeder> logger)
    {
        _db     = db;
        _env    = env;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment())
            throw new InvalidOperationException(
                "CatalogSeeder may only run in Development. Use a controlled bootstrap process for other environments.");

        _logger.LogInformation("Running catalog seeder...");

        // Resolve brand by natural key
        var brand = await _db.Brands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Code == "LG-MAIN", ct);

        if (brand is null)
        {
            _logger.LogWarning("Brand LG-MAIN not found. Run Identity seeder first. Skipping catalog seed.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var brandId = brand.Id;

        // ── Service Categories ────────────────────────────────────────────────

        var (dryCat, laundryCat, steamCat) = await SeedCategoriesAsync(brandId, now, ct);

        // ── Services ──────────────────────────────────────────────────────────

        var (dryCleanSvc, laundrySvc, steamSvc) = await SeedServicesAsync(brandId, dryCat, laundryCat, steamCat, now, ct);

        // ── Fabric Types ──────────────────────────────────────────────────────

        var (cotton, silk, woolen) = await SeedFabricTypesAsync(brandId, now, ct);

        // ── Item Groups ────────────────────────────────────────────────────────

        var (menGroup, womenGroup) = await SeedItemGroupsAsync(brandId, now, ct);

        // ── Items ──────────────────────────────────────────────────────────────

        var (shirt, trouser, saree) = await SeedItemsAsync(brandId, menGroup, womenGroup, now, ct);

        // ── Item Variants ──────────────────────────────────────────────────────

        await SeedItemVariantsAsync(brandId, shirt, trouser, cotton, silk, now, ct);

        // ── Add-On ─────────────────────────────────────────────────────────────

        await SeedAddOnAsync(brandId, now, ct);

        // ── Price List + Items ─────────────────────────────────────────────────

        await SeedPriceListAsync(brandId, dryCleanSvc, laundrySvc, shirt, trouser, now, ct);

        _logger.LogInformation("Catalog seeding complete.");
    }

    // ── Categories ────────────────────────────────────────────────────────────

    private async Task<(ServiceCategory dc, ServiceCategory lc, ServiceCategory sc)> SeedCategoriesAsync(
        Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.ServiceCategories.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .ToDictionaryAsync(x => x.Code, ct);

        ServiceCategory Ensure(string code, string name, string nameLocalizedJson, short order)
        {
            if (existing.TryGetValue(code, out var e)) return e;
            var c = new ServiceCategory
            {
                Id = Guid.NewGuid(), BrandId = brandId, Code = code, Name = name,
                NameLocalized = nameLocalizedJson, DisplayOrder = order,
                IsVisibleMobile = true, IsVisiblePos = true,
                RequiresWarehouseCap = [], Status = "active",
                CreatedAt = now, UpdatedAt = now, Version = 1
            };
            _db.ServiceCategories.Add(c);
            existing[code] = c;
            return c;
        }

        // NameLocalized is jsonb — must be valid JSON
        var dc = Ensure("DRY-CLEAN",  "Dry Clean",  "{\"en\":\"Dry Clean\",\"hi\":\"\\u0921\\u094d\\u0930\\u093e\\u0908 \\u0915\\u094d\\u0932\\u0940\\u0928\"}", 1);
        var lc = Ensure("LAUNDRY",    "Laundry",    "{\"en\":\"Laundry\",\"hi\":\"\\u0932\\u0949\\u0928\\u094d\\u0921\\u094d\\u0930\\u0940\"}", 2);
        var sc = Ensure("STEAM-IRON", "Steam Iron", "{\"en\":\"Steam Iron\",\"hi\":\"\\u0938\\u094d\\u091f\\u0940\\u092e \\u0906\\u092f\\u0930\\u0928\"}", 3);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded service categories.");
        return (dc, lc, sc);
    }

    // ── Services ──────────────────────────────────────────────────────────────

    private async Task<(Service dc, Service lc, Service sc)> SeedServicesAsync(
        Guid brandId,
        ServiceCategory dryCat, ServiceCategory laundryCat, ServiceCategory steamCat,
        DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.Services.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .ToDictionaryAsync(x => x.Code, ct);

        Service Ensure(string code, string name, string nameLocalizedJson, Guid categoryId)
        {
            if (existing.TryGetValue(code, out var e)) return e;
            var s = new Service
            {
                Id = Guid.NewGuid(), BrandId = brandId, CategoryId = categoryId,
                Code = code, Name = name, NameLocalized = nameLocalizedJson,
                PricingModel = "per_item", BaseTatHours = 48, ExpressTatHours = 24,
                ExpressMultiplier = 1.5m, IsExpressAvailable = true,
                RequiresInspection = false, RequiresQc = true,
                DisplayOrder = 1, Status = "active",
                CreatedAt = now, UpdatedAt = now, Version = 1
            };
            _db.Services.Add(s);
            existing[code] = s;
            return s;
        }

        // NameLocalized is jsonb
        var dc = Ensure("SVC-DRY-CLEAN",  "Dry Cleaning",  "{\"en\":\"Dry Cleaning\"}",  dryCat.Id);
        var lc = Ensure("SVC-LAUNDRY",    "Laundry Wash",  "{\"en\":\"Laundry Wash\"}",  laundryCat.Id);
        var sc = Ensure("SVC-STEAM-IRON", "Steam Ironing", "{\"en\":\"Steam Ironing\"}", steamCat.Id);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded services.");
        return (dc, lc, sc);
    }

    // ── Fabric Types ──────────────────────────────────────────────────────────

    private async Task<(FabricType cotton, FabricType silk, FabricType woolen)> SeedFabricTypesAsync(
        Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.FabricTypes.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .ToDictionaryAsync(x => x.Code, ct);

        FabricType Ensure(string code, string name, string nameLocalizedJson, decimal multiplier, short order)
        {
            if (existing.TryGetValue(code, out var e)) return e;
            var f = new FabricType
            {
                Id = Guid.NewGuid(), BrandId = brandId, Code = code, Name = name,
                NameLocalized = nameLocalizedJson, PriceMultiplier = multiplier,
                RequiresSpecialCare = multiplier > 1m, DisplayOrder = order,
                Status = "active", CreatedAt = now, UpdatedAt = now
            };
            _db.FabricTypes.Add(f);
            existing[code] = f;
            return f;
        }

        // NameLocalized is jsonb
        var co = Ensure("COTTON", "Cotton", "{\"en\":\"Cotton\"}", 1.0m, 1);
        var si = Ensure("SILK",   "Silk",   "{\"en\":\"Silk\"}",  1.5m, 2);
        var wo = Ensure("WOOLEN", "Woolen", "{\"en\":\"Woolen\"}", 1.3m, 3);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded fabric types.");
        return (co, si, wo);
    }

    // ── Item Groups ────────────────────────────────────────────────────────────

    private async Task<(ItemGroup men, ItemGroup women)> SeedItemGroupsAsync(
        Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.ItemGroups.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .ToDictionaryAsync(x => x.Code, ct);

        ItemGroup Ensure(string code, string name, string nameLocalizedJson, short order)
        {
            if (existing.TryGetValue(code, out var e)) return e;
            var g = new ItemGroup
            {
                Id = Guid.NewGuid(), BrandId = brandId, Code = code, Name = name,
                NameLocalized = nameLocalizedJson, IsVisibleMobile = true,
                DisplayOrder = order, Status = "active",
                CreatedAt = now, UpdatedAt = now
            };
            _db.ItemGroups.Add(g);
            existing[code] = g;
            return g;
        }

        // NameLocalized is jsonb
        var men   = Ensure("MEN",   "Men",   "{\"en\":\"Men\"}",   1);
        var women = Ensure("WOMEN", "Women", "{\"en\":\"Women\"}", 2);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded item groups.");
        return (men, women);
    }

    // ── Items ──────────────────────────────────────────────────────────────────

    private async Task<(Item shirt, Item trouser, Item saree)> SeedItemsAsync(
        Guid brandId, ItemGroup men, ItemGroup women,
        DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.Items.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .ToDictionaryAsync(x => x.Code, ct);

        Item Ensure(string code, string name, string nameLocalizedJson, Guid? groupId, short order)
        {
            if (existing.TryGetValue(code, out var e)) return e;
            var item = new Item
            {
                Id = Guid.NewGuid(), BrandId = brandId, ItemGroupId = groupId,
                Code = code, Name = name, NameLocalized = nameLocalizedJson,
                RequiresPerSidePrice = false, Aliases = [], DisplayOrder = order,
                Status = "active", CreatedAt = now, UpdatedAt = now, Version = 1
            };
            _db.Items.Add(item);
            existing[code] = item;
            return item;
        }

        // NameLocalized is jsonb
        var shirt   = Ensure("SHIRT",   "Shirt",   "{\"en\":\"Shirt\"}",   men.Id,   1);
        var trouser = Ensure("TROUSER", "Trouser", "{\"en\":\"Trouser\"}", men.Id,   2);
        var saree   = Ensure("SAREE",   "Saree",   "{\"en\":\"Saree\"}",   women.Id, 1);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded items.");
        return (shirt, trouser, saree);
    }

    // ── Item Variants ──────────────────────────────────────────────────────────

    private async Task SeedItemVariantsAsync(
        Guid brandId, Item shirt, Item trouser,
        FabricType cotton, FabricType silk,
        DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.ItemVariants.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .Select(x => x.Code)
            .ToHashSetAsync(ct);

        void Ensure(string code, Guid itemId, Guid? fabricId, string variantName)
        {
            if (existing.Contains(code)) return;
            _db.ItemVariants.Add(new ItemVariant
            {
                Id = Guid.NewGuid(), BrandId = brandId, ItemId = itemId,
                FabricTypeId = fabricId, Code = code, VariantName = variantName,
                DisplayOrder = 1, Status = "active",
                CreatedAt = now, UpdatedAt = now
            });
            existing.Add(code);
        }

        Ensure("SHIRT-COTTON", shirt.Id,   cotton.Id, "Shirt - Cotton");
        Ensure("SHIRT-SILK",   shirt.Id,   silk.Id,   "Shirt - Silk");
        Ensure("TROUSER-STD",  trouser.Id, null,      "Trouser - Standard");
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded item variants.");
    }

    // ── Add-On ─────────────────────────────────────────────────────────────────

    private async Task SeedAddOnAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var exists = await _db.AddOns.IgnoreQueryFilters()
            .AnyAsync(x => x.BrandId == brandId && x.Code == "STAIN-TREAT", ct);
        if (exists) return;

        _db.AddOns.Add(new AddOn
        {
            Id = Guid.NewGuid(), BrandId = brandId,
            Code = "STAIN-TREAT", Name = "Stain Treatment", NameLocalized = "{\"en\":\"Stain Treatment\"}",
            Description = "Professional stain removal",
            PricingType = "flat", PriceValue = 50m,
            ApplicableServices = [], ApplicableCategories = [],
            IsTaxable = true, TaxRatePercent = 18m,
            RequiresApproval = false, DisplayOrder = 1, Status = "active",
            CreatedAt = now, UpdatedAt = now
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded add-on.");
    }

    // ── Price List + Items ─────────────────────────────────────────────────────

    private async Task SeedPriceListAsync(
        Guid brandId, Service dryClean, Service laundry,
        Item shirt, Item trouser,
        DateTimeOffset now, CancellationToken ct)
    {
        const string PriceListCode = "MAIN-BRAND-PL";
        var pl = await _db.PriceLists.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.BrandId == brandId && x.Code == PriceListCode, ct);

        if (pl is null)
        {
            pl = new PriceList
            {
                Id = Guid.NewGuid(), BrandId = brandId,
                Code = PriceListCode, Name = "Main Brand Price List",
                CurrencyCode = "INR", ScopeType = "brand", VersionNumber = 1,
                EffectiveFrom = now, IsDefault = true,
                IsPublished = false, Status = "draft",
                CreatedAt = now, UpdatedAt = now, Version = 1
            };
            _db.PriceLists.Add(pl);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created price list {Id}.", pl.Id);
        }

        // Seed price list items (check by PriceListId + ServiceId + ItemId)
        var existingItems = await _db.PriceListItems
            .Where(x => x.PriceListId == pl.Id)
            .Select(x => new { x.ServiceId, x.ItemId })
            .ToListAsync(ct);
        var existingSet = existingItems.Select(x => (x.ServiceId, x.ItemId)).ToHashSet();

        void EnsureItem(Guid serviceId, Guid itemId, decimal basePrice, decimal? expressPrice)
        {
            if (existingSet.Contains((serviceId, itemId))) return;
            _db.PriceListItems.Add(new PriceListItem
            {
                Id = Guid.NewGuid(), PriceListId = pl.Id, BrandId = brandId,
                ServiceId = serviceId, ItemId = itemId,
                BasePrice = basePrice, ExpressPrice = expressPrice,
                MinimumQuantity = 1, TaxRatePercent = 0m, IsTaxable = false,
                IsActive = true, Status = "active",
                CreatedAt = now, UpdatedAt = now
            });
            existingSet.Add((serviceId, itemId));
        }

        EnsureItem(dryClean.Id, shirt.Id,   150m, 200m);
        EnsureItem(dryClean.Id, trouser.Id, 120m, 180m);
        EnsureItem(laundry.Id,  shirt.Id,    50m,  80m);
        EnsureItem(laundry.Id,  trouser.Id,  60m,  90m);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded price list items.");

        // Publish the price list if not yet published
        if (!pl.IsPublished)
        {
            pl.IsPublished = true;
            pl.PublishedAt = now;
            pl.Status      = "published";
            pl.UpdatedAt   = now;
            pl.Version++;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Published price list {Code}.", PriceListCode);
        }
    }
}
