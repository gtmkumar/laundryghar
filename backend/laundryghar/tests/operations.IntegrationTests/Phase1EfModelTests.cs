using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>
/// Phase-1 gate (slice F-2): the EF model must map the laundry-private attributes into an
/// `attributes` jsonb column (owned type) and NOT carry them as scalar columns on the generic
/// fulfilment-unit spine. Pure model-build assertions — no database connection required.
/// </summary>
public class Phase1EfModelTests
{
    private static LaundryGharDbContext NewContext()
    {
        // Model build only — the connection string is never opened.
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only", o => o.UseNetTopologySuite())
            .Options;
        return new LaundryGharDbContext(options);
    }

    [Theory]
    [InlineData("WeightGrams")]
    [InlineData("HasOrnaments")]
    [InlineData("HasLining")]
    [InlineData("IsDesignerWear")]
    [InlineData("RewashCount")]
    [InlineData("CareInstructions")]
    public void Laundry_attributes_are_not_scalar_columns_on_the_spine(string propertyName)
    {
        using var ctx = NewContext();
        var et = ctx.Model.FindEntityType(typeof(FulfillmentUnit))!;
        Assert.Null(et.FindProperty(propertyName));
    }

    [Fact]
    public void Attributes_is_mapped_to_the_attributes_jsonb_column()
    {
        using var ctx = NewContext();
        var et = ctx.Model.FindEntityType(typeof(FulfillmentUnit))!;

        var nav = et.FindNavigation(nameof(FulfillmentUnit.Attributes));
        Assert.NotNull(nav);

        var owned = nav!.TargetEntityType;
        Assert.True(owned.IsMappedToJson(), "Attributes owned type must be mapped to JSON.");
        Assert.Equal("attributes", owned.GetContainerColumnName());

        // The six attributes live on the owned type.
        foreach (var p in new[] { "WeightGrams", "HasOrnaments", "HasLining", "IsDesignerWear", "RewashCount", "CareInstructions" })
            Assert.NotNull(owned.FindProperty(p));
    }

    [Fact]
    public void FabricTypeId_is_retained_as_a_real_fk_column_on_the_spine()
    {
        // Deliberate deviation from the blueprint's literal list: fabric_type_id is a referential
        // link (warehouse board join), not a free-form attribute, so it stays a real FK column.
        using var ctx = NewContext();
        var et = ctx.Model.FindEntityType(typeof(FulfillmentUnit))!;
        var prop = et.FindProperty(nameof(FulfillmentUnit.FabricTypeId));
        Assert.NotNull(prop);
        Assert.Equal("fabric_type_id", prop!.GetColumnName());
    }
}
