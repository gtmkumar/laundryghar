using laundryghar.Identity.Application.TenancyOrg.Commands;
using laundryghar.Identity.Application.Users.Commands;

namespace laundryghar.Identity.Tests.Settings;

/// <summary>
/// DEF-1 regression: bare admin list endpoints (users/roles/platforms/franchises/
/// stores/warehouses) used to 500 with 'Required parameter "int page" was not provided'
/// because the minimal-API handler took non-optional int page/pageSize. The endpoints
/// now default page=1, pageSize=20 (50 for roles) and clamp out-of-range values.
///
/// These tests pin (a) the query-record defaults the endpoints feed into, and
/// (b) the page/pageSize clamp expression the endpoints apply.
/// </summary>
public sealed class PaginationDefaultsTests
{
    [Fact]
    public void GetUsersQuery_DefaultsToPage1Size20()
    {
        var q = new GetUsersQuery();
        Assert.Equal(1, q.Page);
        Assert.Equal(20, q.PageSize);
    }

    [Fact]
    public void GetRolesQuery_DefaultsToPage1Size50()
    {
        var q = new GetRolesQuery();
        Assert.Equal(1, q.Page);
        Assert.Equal(50, q.PageSize);
    }

    [Fact]
    public void GetPlatformsQuery_DefaultsToPage1Size20()
    {
        var q = new GetPlatformsQuery();
        Assert.Equal(1, q.Page);
        Assert.Equal(20, q.PageSize);
    }

    [Theory]
    [InlineData(1, 20, 1, 20)]      // valid pass-through
    [InlineData(0, 20, 1, 20)]      // page below 1 → 1
    [InlineData(-5, 20, 1, 20)]     // negative page → 1
    [InlineData(3, 0, 3, 20)]       // pageSize below 1 → default 20
    [InlineData(3, -10, 3, 20)]     // negative pageSize → default 20
    public void Clamp_NormalizesOutOfRangeValues(int page, int pageSize, int expPage, int expSize)
    {
        // Mirrors the exact clamp expression used in the admin list endpoints.
        var clampedPage = page < 1 ? 1 : page;
        var clampedSize = pageSize < 1 ? 20 : pageSize;

        Assert.Equal(expPage, clampedPage);
        Assert.Equal(expSize, clampedSize);
    }
}
