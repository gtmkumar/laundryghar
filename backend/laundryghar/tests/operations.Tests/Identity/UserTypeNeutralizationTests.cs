using laundryghar.SharedDataModel.Enums;
using Xunit;

namespace operations.Tests.Identity;

/// <summary>Phase-2 slice 2D: the user_type vocabulary gains a vertical-neutral ops-staff type
/// while retaining the laundry-specific warehouse_staff for data compatibility.</summary>
public class UserTypeNeutralizationTests
{
    [Fact]
    public void Neutral_ops_staff_and_legacy_warehouse_staff_are_both_valid()
    {
        Assert.True(UserType.IsValid(UserType.OpsStaff));
        Assert.True(UserType.IsValid(UserType.WarehouseStaff));
        Assert.Equal("ops_staff", UserType.OpsStaff);
        Assert.Equal("warehouse_staff", UserType.WarehouseStaff);
    }

    [Fact]
    public void Both_processing_staff_types_are_operational()
    {
        Assert.True(UserType.IsOperationalStaff(UserType.OpsStaff));
        Assert.True(UserType.IsOperationalStaff(UserType.WarehouseStaff));
        Assert.False(UserType.IsOperationalStaff(UserType.Rider));
        Assert.False(UserType.IsOperationalStaff(UserType.BrandAdmin));
    }

    [Fact]
    public void IsValid_rejects_unknown_types()
    {
        Assert.False(UserType.IsValid("stylist"));
        Assert.False(UserType.IsValid(null));
    }
}
