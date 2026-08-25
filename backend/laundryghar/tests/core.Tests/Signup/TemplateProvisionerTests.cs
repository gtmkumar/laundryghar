using core.Application.Identity.Signup;
using laundryghar.SharedDataModel.Enums;
using Xunit;

namespace core.Tests.Signup;

/// <summary>
/// The pure half of provisioning a new provider from a template (§7 "live in minutes").
///
/// <para>The catalogue seed is authored data that ships in a migration, so the important behaviour
/// is what happens when it is WRONG: a signup must never fail because someone mistyped a seed. A
/// provider with an empty catalogue can add items; a provider who could not sign up is gone.</para>
/// </summary>
public class TemplateProvisionerTests
{
    [Fact]
    public void a_well_formed_seed_becomes_categories_and_items()
    {
        var seed = TemplateProvisioner.ParseSeed(
            """[{"category":"Wash & Fold","items":["Shirt","Trouser"]},{"category":"Ironing","items":["Shirt"]}]""");

        Assert.Equal(2, seed.Count);
        Assert.Equal("Wash & Fold", seed[0].Category);
        Assert.Equal(["Shirt", "Trouser"], seed[0].Items);
        Assert.Equal(["Shirt"], seed[1].Items);
    }

    // A broken seed degrades to nothing rather than throwing — see the class comment.
    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"noCategory":"x"}]""")]
    [InlineData("""[{"category":""}]""")]
    [InlineData("""[{"category":"   "}]""")]
    [InlineData("""{"category":"not an array"}""")]
    public void a_malformed_or_empty_seed_yields_nothing_rather_than_throwing(string json)
        => Assert.Empty(TemplateProvisioner.ParseSeed(json));

    [Fact]
    public void a_category_with_no_items_is_still_created()
    {
        // An empty category is a usable starting point — the provider adds their own items into it.
        var seed = TemplateProvisioner.ParseSeed("""[{"category":"Specials"}]""");

        Assert.Single(seed);
        Assert.Empty(seed[0].Items);
    }

    // Item codes are built from these and must be unique per brand, so the slug carries real weight.
    [Theory]
    [InlineData("Wash & Fold", "wash-fold")]
    [InlineData("Dry Clean", "dry-clean")]
    [InlineData("  Ironing  ", "ironing")]
    [InlineData("Veg thali", "veg-thali")]
    public void slugs_are_stable_and_url_safe(string input, string expected)
        => Assert.Equal(expected, TemplateProvisioner.Slug(input));

    // "Shirt" legitimately appears under both Wash & Fold and Ironing; the code namespaces by
    // category so the two do not collide on the brand-unique item code.
    [Fact]
    public void the_same_item_in_two_categories_produces_different_codes()
    {
        var washFold = $"{TemplateProvisioner.Slug("Wash & Fold")}-{TemplateProvisioner.Slug("Shirt")}";
        var ironing = $"{TemplateProvisioner.Slug("Ironing")}-{TemplateProvisioner.Slug("Shirt")}";

        Assert.NotEqual(washFold, ironing);
        Assert.Equal("wash-fold-shirt", washFold);
    }

    // What a catalogue row IS differs by vertical — a garment is not a service is not a parcel.
    [Theory]
    [InlineData(VerticalKey.Laundry, CatalogKind.LaundryGarment)]
    [InlineData(VerticalKey.Salon, CatalogKind.Service)]
    [InlineData(VerticalKey.Logistics, CatalogKind.Parcel)]
    [InlineData(VerticalKey.Tiffin, CatalogKind.Product)]
    [InlineData("something-new", CatalogKind.LaundryGarment)]  // unknown falls back to the flagship
    public void the_catalog_kind_follows_the_vertical(string vertical, string expected)
        => Assert.Equal(expected, TemplateProvisioner.CatalogKindFor(vertical));
}
