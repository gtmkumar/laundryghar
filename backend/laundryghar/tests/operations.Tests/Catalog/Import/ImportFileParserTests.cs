using System.Text;
using operations.Application.Catalog.Catalog.Import;
using Xunit;

namespace operations.Tests.Catalog.Import;

/// <summary>
/// Parser coverage for the #20 bulk-import: the real legacy rate-list workbook (sheet→service mapping,
/// category extraction, per-kg sheets, junk-sheet skipping, cross-sheet dedupe) and the flat CSV layout
/// (fabric columns, Tax%, slug code generation).
/// </summary>
public class ImportFileParserTests
{
    private static ImportFileParser.ParsedImport ParseCsv(string csv)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return ImportFileParser.Parse(ms, "items.csv");
    }

    // ── Legacy workbook ─────────────────────────────────────────────────────────

    [Fact]
    public void Legacy_workbook_maps_sheets_to_services_and_extracts_categories()
    {
        using var fs = File.OpenRead(ImportTestSupport.FixturePath);
        var result = ImportFileParser.Parse(fs, "RateListForDrycleaning.xlsx");

        Assert.Equal(ImportFileParser.LayoutLegacy, result.Layout);

        // Sheet name becomes the service; the same item across sheets folds into one row.
        var belt = Assert.Single(result.Rows.Where(r => r.Code == "BELT"));
        Assert.Equal("Belt", belt.Name);
        Assert.Equal("Mens", belt.Category); // first pair (Mens) wins for category

        var dryClean = Assert.Single(belt.ServicePrices.Where(sp => sp.ServiceName == "Dry Cleaning"));
        Assert.Equal(59m, dryClean.BasePrice);
        var steam = Assert.Single(belt.ServicePrices.Where(sp => sp.ServiceName == "Steam Press"));
        Assert.Equal(29m, steam.BasePrice);
    }

    [Fact]
    public void Legacy_workbook_reads_per_kg_sheets()
    {
        using var fs = File.OpenRead(ImportTestSupport.FixturePath);
        var result = ImportFileParser.Parse(fs, "RateListForDrycleaning.xlsx");

        var premium = Assert.Single(result.Rows.Where(r => r.Name == "Premium Laundry Kg"));
        var price = Assert.Single(premium.ServicePrices.Where(sp => sp.ServiceName == "Premium Laundry Kg"));
        Assert.Equal(185m, price.BasePrice);
        Assert.Null(premium.Category); // per-kg "Item list" header carries no category
    }

    [Fact]
    public void Legacy_workbook_skips_junk_sheets_with_warnings()
    {
        using var fs = File.OpenRead(ImportTestSupport.FixturePath);
        var result = ImportFileParser.Parse(fs, "RateListForDrycleaning.xlsx");

        // "Sheet1" (empty) and "Sheet2" (Item/Amount header) match no legacy pattern → reported, not parsed.
        Assert.Contains(result.Errors, e => e.Sheet == "Sheet1");
        Assert.Contains(result.Errors, e => e.Sheet == "Sheet2");

        // No row should carry a Sheet1/Sheet2 "service".
        Assert.DoesNotContain(result.Rows, r => r.ServicePrices.Any(sp => sp.ServiceName is "Sheet1" or "Sheet2"));
    }

    [Fact]
    public void Legacy_workbook_flags_conflicting_prices_for_same_slug()
    {
        using var fs = File.OpenRead(ImportTestSupport.FixturePath);
        var result = ImportFileParser.Parse(fs, "RateListForDrycleaning.xlsx");

        // "Blouse" appears under both Womens (99) and Kids (39) of Dry Cleaning — same slug, same service.
        Assert.Contains(result.Errors, e => e.Message.Contains("conflicting", StringComparison.OrdinalIgnoreCase));
    }

    // ── Flat CSV ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Flat_csv_parses_base_fabric_and_tax_columns()
    {
        var result = ParseCsv(
            "Code,Name,Category,Status,TAT,Tax%,Dry Clean,Dry Clean:Silk,Wash\n" +
            "SHIRT,Shirt,Mens,active,24,5,49,89,30\n");

        Assert.Equal(ImportFileParser.LayoutFlat, result.Layout);
        var row = Assert.Single(result.Rows);
        Assert.Equal("SHIRT", row.Code);
        Assert.Equal("Shirt", row.Name);
        Assert.Equal("Mens", row.Category);
        Assert.Equal("active", row.Status);
        Assert.Equal(24, row.TatHours);
        Assert.Equal(5m, row.TaxRatePercent);

        var dryClean = Assert.Single(row.ServicePrices.Where(sp => sp.ServiceName == "Dry Clean"));
        Assert.Equal(49m, dryClean.BasePrice);
        var silk = Assert.Single(dryClean.FabricPrices!);
        Assert.Equal("Silk", silk.FabricName);
        Assert.Equal(89m, silk.Price);

        var wash = Assert.Single(row.ServicePrices.Where(sp => sp.ServiceName == "Wash"));
        Assert.Equal(30m, wash.BasePrice);
        Assert.True(wash.FabricPrices is null || wash.FabricPrices.Count == 0);
    }

    [Fact]
    public void Flat_csv_slugs_code_from_name_when_code_blank()
    {
        var result = ParseCsv("Code,Name,Wash\n,Nice Shirt,30\n");
        var row = Assert.Single(result.Rows);
        Assert.Equal("NICE-SHIRT", row.Code);
    }

    [Fact]
    public void Flat_csv_reports_row_with_missing_name()
    {
        var result = ParseCsv("Code,Name,Wash\nSHIRT,,30\n");
        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, e => e.Message.Contains("Name is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Flat_csv_ignores_non_numeric_price_with_warning()
    {
        var result = ParseCsv("Code,Name,Wash\nSHIRT,Shirt,notaprice\n");
        var row = Assert.Single(result.Rows);
        Assert.Empty(row.ServicePrices);
        Assert.Contains(result.Errors, e => e.Message.Contains("not a number", StringComparison.OrdinalIgnoreCase));
    }

    // ── Slug generation ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Blazer / Coat - Long", "BLAZER-COAT-LONG")]
    [InlineData("Women's Shoulder Bag", "WOMEN-S-SHOULDER-BAG")]
    [InlineData("  Premium  ", "PREMIUM")]
    [InlineData("T - shirt", "T-SHIRT")]
    public void Slugify_produces_stable_uppercase_codes(string name, string expected)
        => Assert.Equal(expected, ImportFileParser.SlugifyCode(name));
}
