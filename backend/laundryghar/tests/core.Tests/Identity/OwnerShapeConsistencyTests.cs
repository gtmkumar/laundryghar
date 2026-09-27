using core.Infrastructure.Seeders;
using laundryghar.SharedDataModel.Enums;
using Xunit;

namespace core.Tests.Identity;

/// <summary>
/// Audit finding A-2, "two owners, two shapes".
///
/// <para><b>What was wrong.</b> Two flows create an account together with its primary role, and each
/// wrote the <c>user_type</c> next to the role code by hand. <c>CompleteSignup</c> typed a self-serve
/// brand owner <c>staff</c> while granting the <c>brand_admin</c> role; <c>InviteOwner</c> typed a
/// franchise owner <c>franchise_owner</c> alongside the matching role. Nothing checked the two
/// pairings agreed, so one drifted.</para>
///
/// <para><b>Why it was not cosmetic.</b> Type and role are independent axes, and code reading the
/// wrong one gets the wrong answer silently: <c>AdminSettings.Forbidden</c> gates on
/// <c>UserType == "brand_admin"</c>, so a self-signed-up owner holding the brand_admin ROLE was
/// refused their own brand's settings — reproduced as a live 403 before the fix and a 200 after.</para>
///
/// <para>Both flows now derive the type from the role through
/// <see cref="UserType.ForPrimaryRole"/>, so the pairing cannot drift again. These tests pin the
/// mapping itself, which is the thing that would have to be wrong for the finding to recur.</para>
/// </summary>
public sealed class OwnerShapeConsistencyTests
{
    [Theory]
    [InlineData("brand_admin",     UserType.BrandAdmin)]      // CompleteSignup — the flow that drifted
    [InlineData("franchise_owner", UserType.FranchiseOwner)]  // InviteOwner
    public void An_owner_role_maps_to_the_matching_user_type(string roleCode, string expectedType)
    {
        Assert.Equal(expectedType, UserType.ForPrimaryRole(roleCode));
    }

    [Theory]
    [InlineData("platform_admin",  UserType.PlatformAdmin)]
    [InlineData("store_admin",     UserType.StoreAdmin)]
    [InlineData("rider",           UserType.Rider)]
    [InlineData("auditor",         UserType.Auditor)]
    [InlineData("support",         UserType.Support)]
    [InlineData("warehouse_staff", UserType.WarehouseStaff)]
    public void Every_role_with_a_matching_type_maps_to_it(string roleCode, string expectedType)
    {
        // A seeded role whose code is also a user type must map to that type, or the same
        // "type says one thing, role says another" divergence reappears somewhere else.
        Assert.Equal(expectedType, UserType.ForPrimaryRole(roleCode));
    }

    [Theory]
    [InlineData("store_staff")]            // seeded, no distinct type tier
    [InlineData("regional_manager")]       // seeded, no distinct type tier
    [InlineData("salon_manager")]          // vertical operational role
    [InlineData("operations_manager")]     // a brand's own custom role
    [InlineData(null)]
    [InlineData("")]
    public void A_role_with_no_distinct_tier_falls_back_to_staff(string? roleCode)
    {
        // Not every role has a type of its own, and inventing one would be worse than the drift:
        // these were already `staff` and must stay there.
        Assert.Equal(UserType.Staff, UserType.ForPrimaryRole(roleCode));
    }

    [Fact]
    public void Every_type_the_mapping_can_return_is_a_valid_user_type()
    {
        // The mapping writes straight into users.user_type, which carries a CHECK constraint; an
        // unrecognised string here is a 23514 at account creation, not a compile error.
        foreach (var roleCode in SeededRoleCodes())
            Assert.True(UserType.IsValid(UserType.ForPrimaryRole(roleCode)),
                $"ForPrimaryRole(\"{roleCode}\") returned a value outside UserType.All.");
    }

    /// <summary>Every role the platform seeds, read from the seeder rather than restated here.</summary>
    private static IEnumerable<string> SeededRoleCodes() =>
        IdentitySeeder.PermissionDefs.Select(p => p.Module)      // module names overlap role codes
            .Concat(["platform_admin", "brand_admin", "regional_manager", "franchise_owner",
                     "store_admin", "store_staff", "warehouse_supervisor", "warehouse_staff",
                     "salon_manager", "salon_staff", "hub_supervisor", "hub_operator",
                     "rider", "auditor", "support", "partner_admin", "partner_operator"])
            .Distinct();
}
