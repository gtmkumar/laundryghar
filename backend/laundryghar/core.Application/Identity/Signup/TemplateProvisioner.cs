using System.Text.Json;
using core.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;

namespace core.Application.Identity.Signup;

/// <summary>
/// Turns a picked <see cref="VerticalTemplate"/> into a working business — the substance behind §7's
/// "create provider → pick vertical template → pick plan → live on sub-domain in minutes".
///
/// <para>Three things happen, in this order and for a reason:</para>
/// <list type="number">
/// <item><b>Features</b> — expand the template's default tier into <c>brand_feature</c>. First,
/// because entitlement enforcement is ON: a brand with no features is a brand whose staff log in to
/// an empty console.</item>
/// <item><b>Catalogue</b> — seed the preset categories and items so the first screen is not blank.
/// Deliberately WITHOUT prices: §8.1 is explicit that the platform does not set a company's customer
/// prices. Providers get the shape, and fill in their own numbers.</item>
/// <item><b>Terminology</b> — nothing to do; it is already per-vertical (migration 0010) and the
/// brand's <c>vertical_key</c> selects it. Noted so its absence here does not read as an omission.</item>
/// </list>
/// </summary>
public sealed class TemplateProvisioner
{
    private readonly ICoreDbContext _db;

    public TemplateProvisioner(ICoreDbContext db) => _db = db;

    public sealed record Result(string? BundleCode, int CategoriesCreated, int ItemsCreated);

    /// <summary>
    /// Provisions <paramref name="brandId"/> from <paramref name="template"/>. Adds entities to the
    /// context but does NOT save — the caller owns the transaction, so a half-provisioned brand
    /// cannot survive a failure partway through.
    /// </summary>
    public async Task<Result> ApplyAsync(
        Guid brandId, VerticalTemplate template, Guid? actorId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var featureCount = await ApplyDefaultTierAsync(brandId, template, actorId, now, ct);
        var (categories, items) = SeedCatalog(brandId, template, actorId, now);

        return new Result(
            featureCount > 0 ? template.DefaultBundleCode : null,
            categories, items);
    }

    /// <summary>Expands the template's default bundle into brand_feature rows, honouring the vertical
    /// gate so a shared tier never licenses a laundry-only feature to a salon.</summary>
    private async Task<int> ApplyDefaultTierAsync(
        Guid brandId, VerticalTemplate template, Guid? actorId, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(template.DefaultBundleCode)) return 0;

        var featureKeys = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(
                from bf in _db.BundleFeatures
                join f in _db.Features on bf.FeatureKey equals f.Key
                where bf.BundleCode == template.DefaultBundleCode
                      && f.Status == "active"
                      && (f.VerticalKey == null || f.VerticalKey == template.VerticalKey)
                select f.Key, ct);

        foreach (var key in featureKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _db.BrandFeatures.Add(new BrandFeature
            {
                BrandId = brandId, FeatureKey = key, Enabled = true,
                // 'bundle', not 'manual': a later plan change must be free to re-expand these.
                Source = "bundle",
                CreatedAt = now, UpdatedAt = now, CreatedBy = actorId, UpdatedBy = actorId,
            });
        }

        return featureKeys.Count;
    }

    /// <summary>
    /// Creates the preset categories and items. Prices are deliberately absent — see the class
    /// comment. A malformed seed produces an empty catalogue rather than a failed signup: losing a
    /// convenience is acceptable, losing the customer at the door is not.
    /// </summary>
    private (int Categories, int Items) SeedCatalog(
        Guid brandId, VerticalTemplate template, Guid? actorId, DateTimeOffset now)
    {
        List<(string Category, List<string> Items)> seed;
        try
        {
            seed = ParseSeed(template.CatalogSeed);
        }
        catch (JsonException)
        {
            return (0, 0);
        }

        var catalogKind = CatalogKindFor(template.VerticalKey);
        int categories = 0, items = 0;
        short categoryOrder = 0;

        foreach (var (categoryName, itemNames) in seed)
        {
            var category = new ServiceCategory
            {
                Id = Guid.NewGuid(), BrandId = brandId,
                Code = Slug(categoryName), Name = categoryName,
                NameLocalized = Localized(categoryName),
                DisplayOrder = categoryOrder++,
                IsVisibleMobile = true, IsVisiblePos = true,
                RequiresWarehouseCap = [],
                Status = "active",
                CreatedAt = now, UpdatedAt = now, CreatedBy = actorId, UpdatedBy = actorId, Version = 1,
            };
            _db.ServiceCategories.Add(category);
            categories++;

            short itemOrder = 0;
            foreach (var itemName in itemNames)
            {
                _db.Items.Add(new Item
                {
                    Id = Guid.NewGuid(), BrandId = brandId,
                    CatalogKind = catalogKind,
                    Attributes = "{}",
                    PricingMode = PricingMode.Standard,
                    // Namespaced by category: "Shirt" legitimately appears under both Wash & Fold and
                    // Ironing, and item codes are unique per brand.
                    Code = $"{Slug(categoryName)}-{Slug(itemName)}",
                    Name = itemName,
                    NameLocalized = Localized(itemName),
                    Aliases = [],
                    DisplayOrder = itemOrder++,
                    Status = "active",
                    CreatedAt = now, UpdatedAt = now, CreatedBy = actorId, UpdatedBy = actorId, Version = 1,
                });
                items++;
            }
        }

        return (categories, items);
    }

    /// <summary>What a catalogue row IS in this vertical — a garment, a service, a parcel.</summary>
    internal static string CatalogKindFor(string verticalKey) => verticalKey switch
    {
        VerticalKey.Salon     => CatalogKind.Service,
        VerticalKey.Logistics => CatalogKind.Parcel,
        VerticalKey.Tiffin    => CatalogKind.Product,   // a meal is a product, not a service performed
        _                     => CatalogKind.LaundryGarment,
    };

    /// <summary>Internal for testing — a malformed seed must degrade, not fail a signup.</summary>
    internal static List<(string Category, List<string> Items)> ParseSeed(string json)
    {
        var result = new List<(string, List<string>)>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("category", out var c)) continue;
            var name = c.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var itemNames = new List<string>();
            if (entry.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                itemNames.AddRange(itemsEl.EnumerateArray()
                    .Select(i => i.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))!
                    .Cast<string>());

            result.Add((name!, itemNames));
        }

        return result;
    }

    /// <summary>The bilingual shape the catalogue stores (en-IN/hi-IN). Seeded English-only: an
    /// auto-translated Hindi name would be worse than an honest untranslated one.</summary>
    private static string Localized(string value) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["en-IN"] = value });

    /// <summary>Internal for testing — slugs become brand-unique item codes.</summary>
    internal static string Slug(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');

        // Collapse runs in a LOOP, not one pass. "Wash & Fold" maps to "wash---fold", and a single
        // Replace("--","-") leaves "wash--fold" — a real defect caught by the slug test, and one that
        // would have been baked into every seeded item code as a permanent cosmetic wart.
        while (slug.Contains("--")) slug = slug.Replace("--", "-");

        return slug;
    }
}
