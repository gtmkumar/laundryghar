using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Services;
using Xunit;

namespace operations.Tests.Auth;

/// <summary>
/// Locks in the §6 ancestor-or-self boundary check exposed by <c>HttpContextCurrentUser.IsWithinScope</c>.
/// The claim layer is EXACT-level (each node only matches its own level); the hierarchy widening
/// (a brand membership covering its stores) is applied elsewhere by callers passing the full chain.
/// </summary>
public class ScopeBoundaryTests
{
    private static ICurrentUser CurrentUser(
        string? scopeNodes = null, string? userType = null, string? scopeType = null)
        => new HttpContextCurrentUser(
            RbacTestSupport.AccessorFor(
                RbacTestSupport.Principal(
                    tokenUse: "user", userType: userType, scopeNodes: scopeNodes, scopeType: scopeType)));

    // Test 1 — absent scope_nodes claim fails OPEN (rollout safety); a present-but-empty claim denies.
    [Fact]
    public void Absent_scope_nodes_allows_but_empty_string_denies()
    {
        var anyStore = Guid.NewGuid();

        // Claim omitted entirely → not enforceable → allow.
        var noClaim = CurrentUser(scopeNodes: null);
        Assert.True(noClaim.IsWithinScope(storeId: anyStore));

        // Claim present but empty → enforced, zero nodes → deny.
        var emptyClaim = CurrentUser(scopeNodes: "");
        Assert.False(emptyClaim.IsWithinScope(storeId: anyStore));
    }

    // Test 2 — a single store node matches ONLY that store; it does not widen to a sibling store
    // nor up to the brand (the claim layer is exact-level).
    [Fact]
    public void Store_node_matches_exactly_that_store_only()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var brand = Guid.NewGuid();

        var user = CurrentUser(scopeNodes: $"{ScopeType.Store}:{s1}");

        Assert.True(user.IsWithinScope(storeId: s1));   // exact node → allow
        Assert.False(user.IsWithinScope(storeId: s2));  // sibling store → deny
        Assert.False(user.IsWithinScope(brandId: brand)); // brand ancestor NOT expressed at claim layer → deny
    }

    // Test 2 (cont.) — a platform node, and a platform_admin user_type, are unbounded.
    [Fact]
    public void Platform_scope_and_platform_admin_are_unbounded()
    {
        var target = Guid.NewGuid();

        // "platform" node → the switch returns true for every target.
        var platformNode = CurrentUser(scopeNodes: ScopeType.Platform);
        Assert.True(platformNode.IsWithinScope(storeId: target));
        Assert.True(platformNode.IsWithinScope(brandId: target));

        // user_type=platform_admin short-circuits IsPlatformAdmin → true for any target,
        // even with no scope_nodes claim at all.
        var platformAdmin = CurrentUser(userType: UserType.PlatformAdmin);
        Assert.True(platformAdmin.IsWithinScope(storeId: target));
        Assert.True(platformAdmin.IsWithinScope(brandId: target));
    }
}
