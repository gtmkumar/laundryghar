using laundryghar.SharedDataModel.Enums;
using Xunit;

namespace operations.Tests.Catalog;

/// <summary>
/// Phase-2 slice 2B: the vertical gate that hides laundry-only modules (e.g. fabric management)
/// from non-laundry brands. This is the pure predicate GetNavigator applies per module.
/// </summary>
public class VerticalModuleGatingTests
{
    [Fact]
    public void Neutral_module_is_visible_to_every_brand()
    {
        Assert.True(VerticalKey.IsAvailableTo(null, VerticalKey.Laundry));
        Assert.True(VerticalKey.IsAvailableTo(null, VerticalKey.Salon));
        Assert.True(VerticalKey.IsAvailableTo(null, null));
    }

    [Fact]
    public void Laundry_module_is_visible_only_to_laundry_brands()
    {
        Assert.True(VerticalKey.IsAvailableTo(VerticalKey.Laundry, VerticalKey.Laundry));
        Assert.False(VerticalKey.IsAvailableTo(VerticalKey.Laundry, VerticalKey.Salon));
        Assert.False(VerticalKey.IsAvailableTo(VerticalKey.Laundry, VerticalKey.Logistics));
    }

    [Fact]
    public void Platform_admin_with_no_brand_sees_all_modules()
    {
        // brandVertical == null → every module passes the vertical gate.
        Assert.True(VerticalKey.IsAvailableTo(VerticalKey.Laundry, null));
        Assert.True(VerticalKey.IsAvailableTo(VerticalKey.Salon, null));
    }

    [Theory]
    [InlineData("LAUNDRY", "laundry")]
    [InlineData("laundry", "LAUNDRY")]
    public void Vertical_match_is_case_insensitive(string moduleVertical, string brandVertical)
        => Assert.True(VerticalKey.IsAvailableTo(moduleVertical, brandVertical));
}
