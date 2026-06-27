using laundryghar.SharedDataModel.Enums;
using Xunit;

namespace operations.Tests.Commerce;

/// <summary>Phase-2 slice 2E: subscription quota gains the vertical-neutral job_count unit
/// while retaining the laundry-flavoured order_count.</summary>
public class QuotaUnitTests
{
    [Fact]
    public void Neutral_job_count_and_legacy_order_count_are_both_valid()
    {
        Assert.True(QuotaUnit.IsValid(QuotaUnit.JobCount));
        Assert.True(QuotaUnit.IsValid(QuotaUnit.OrderCount));
        Assert.Equal("job_count", QuotaUnit.JobCount);
    }

    [Theory]
    [InlineData("credit")]
    [InlineData("weight_kg")]
    [InlineData("unlimited")]
    public void Existing_units_remain_valid(string unit) => Assert.True(QuotaUnit.IsValid(unit));

    [Fact]
    public void Unknown_unit_is_rejected() => Assert.False(QuotaUnit.IsValid("per_appointment"));
}
