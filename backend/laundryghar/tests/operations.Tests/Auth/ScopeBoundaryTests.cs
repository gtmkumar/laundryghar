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

    // Test 1 (A7.4) — BOTH an absent scope_nodes claim and a present-but-empty one deny.
    //
    // This test previously asserted the opposite for the absent case: a token with no scope_nodes
    // claim passed every scope check, on the rationale that such a token could only predate the
    // feature and should not be locked out mid-session. That made "a token missing its
    // authorization data" the broadest fail-open in the model. The claim has been minted for every
    // user token for many releases and access tokens are short-lived, so the rollout window it was
    // protecting has long closed — and the case it was really protecting (a principal with no
    // memberships) is the empty-claim case, which denied correctly all along.
    [Fact]
    public void Absent_and_empty_scope_nodes_both_deny()
    {
        var anyStore = Guid.NewGuid();

        // Claim omitted entirely → cannot be evaluated → deny.
        var noClaim = CurrentUser(scopeNodes: null);
        Assert.False(noClaim.IsWithinScope(storeId: anyStore));

        // Claim present but empty → enforced, zero nodes → deny.
        var emptyClaim = CurrentUser(scopeNodes: "");
        Assert.False(emptyClaim.IsWithinScope(storeId: anyStore));
    }

    // Test 1b (A7.3) — platform SCOPE is no longer a master key; platform MEMBERSHIP still is.
    //
    // IsPlatformAdmin used to be satisfied by `scope_type == platform`, which meant any principal
    // whose active membership sat at the platform node — a read-only auditor included — passed
    // every scope check and every one of the 25 other IsPlatformAdmin guards. Authority now comes
    // from the membership evidence in scope_nodes, or from user_type, and not from which
    // membership the caller happens to be operating through.
    [Fact]
    public void Platform_scope_type_alone_no_longer_grants_unbounded_access()
    {
        var anyStore = Guid.NewGuid();

        // scope_type=platform but NO platform node and a non-admin user_type → deny.
        var scopeTypeOnly = CurrentUser(
            scopeNodes: "", userType: UserType.Auditor, scopeType: ScopeType.Platform);
        Assert.False(scopeTypeOnly.IsPlatformAdmin);
        Assert.False(scopeTypeOnly.IsWithinScope(storeId: anyStore));

        // A real platform MEMBERSHIP still passes, via the node rather than the master key.
        var platformNode = CurrentUser(
            scopeNodes: ScopeType.Platform, userType: UserType.Auditor);
        Assert.False(platformNode.IsPlatformAdmin);
        Assert.True(platformNode.IsWithinScope(storeId: anyStore));
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
