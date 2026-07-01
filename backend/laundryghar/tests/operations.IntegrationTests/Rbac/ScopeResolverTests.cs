using core.Application.Identity.Auth.Common;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>EF-faithful lock-in of ScopeResolver (docs/rbac.md #10 — deny-wins set-math, §6 ancestor
/// union, expiry/revocation, scoped overrides, §8 step-up, scope_nodes) against a REAL Postgres.</summary>
[Collection("rbac-ef")]
public sealed class ScopeResolverTests
{
    private readonly RbacEfFixture _fx;
    public ScopeResolverTests(RbacEfFixture fx) => _fx = fx;

    // 1 ── deny wins at the role level AND the user-override level; a user allow adds a new code.
    [Fact]
    public async Task deny_wins_role_and_user_override()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var royaltyOverride = Perm($"royalty.override.{t}");
        var keep            = Perm($"orders.read.{t}");
        var userDenied      = Perm($"orders.refund.{t}");
        var userAdded       = Perm($"reports.export.{t}");

        var allowRole = RoleRow($"broad.{t}");
        var denyRole  = RoleRow($"exception.{t}");
        // role_permissions has UNIQUE(role_id, permission_id), so the allow+deny for the SAME code
        // must come from two roles the user holds — that is the role-level deny-wins path.
        var rps = new[]
        {
            RP(allowRole.Id, royaltyOverride.Id, "allow"),
            RP(allowRole.Id, keep.Id,            "allow"),
            RP(allowRole.Id, userDenied.Id,      "allow"),
            RP(denyRole.Id,  royaltyOverride.Id, "deny"),
        };

        var user = Usr();
        var mems = new[]
        {
            Member(user.Id, allowRole.Id, ScopeType.Platform, null, primary: true),
            Member(user.Id, denyRole.Id,  ScopeType.Platform, null),
        };
        var overrides = new[]
        {
            Override(user.Id, userDenied.Id, "deny"),  // beats the role allow
            Override(user.Id, userAdded.Id,  "allow"), // adds a code no role grants
        };

        await SeedAsync(new object[] { royaltyOverride, keep, userDenied, userAdded, allowRole, denyRole, user }
            .Concat(rps).Concat(mems).Concat(overrides));

        var perms = await ResolveAsync(user);

        Assert.Contains(keep.Code, perms);
        Assert.Contains(userAdded.Code, perms);
        Assert.DoesNotContain(royaltyOverride.Code, perms); // role deny beats role allow
        Assert.DoesNotContain(userDenied.Code, perms);      // user deny beats role allow
    }

    // 2 ── §6 ancestor union: a brand role covers a store beneath it; if the store row is absent the
    //       brand can't be resolved and the brand membership drops out.
    [Fact]
    public async Task ancestor_union_brand_over_store()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        var franchiseId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        await _fx.SeedStoreChainAsync(brandId, franchiseId, storeId);

        var permX = Perm($"store.perm.{t}");   // from the store-scoped role
        var permY = Perm($"brand.perm.{t}");   // from the brand-scoped role (ancestor)
        var storeRole = RoleRow($"store_role.{t}", ScopeType.Store);
        var brandRole = RoleRow($"brand_role.{t}", ScopeType.Brand);
        var user = Usr();
        await SeedAsync(new object[]
        {
            permX, permY, storeRole, brandRole, user,
            RP(storeRole.Id, permX.Id), RP(brandRole.Id, permY.Id),
            Member(user.Id, storeRole.Id, ScopeType.Store, storeId, primary: true),
            Member(user.Id, brandRole.Id, ScopeType.Brand, brandId),
        });

        var perms = await ResolveAsync(user, ScopeType.Store, storeId);
        Assert.Contains(permX.Code, perms); // self
        Assert.Contains(permY.Code, perms); // ancestor brand covers the store

        // Negative: the SAME membership shape but the active store row is absent → brand unresolved →
        // the brand membership is NOT in the ancestor set, so its perm drops.
        var t2 = Tag();
        var unseededStore = Guid.NewGuid();
        var permX2 = Perm($"store.perm.{t2}");
        var permY2 = Perm($"brand.perm.{t2}");
        var storeRole2 = RoleRow($"store_role.{t2}", ScopeType.Store);
        var brandRole2 = RoleRow($"brand_role.{t2}", ScopeType.Brand);
        var user2 = Usr();
        await SeedAsync(new object[]
        {
            permX2, permY2, storeRole2, brandRole2, user2,
            RP(storeRole2.Id, permX2.Id), RP(brandRole2.Id, permY2.Id),
            Member(user2.Id, storeRole2.Id, ScopeType.Store, unseededStore, primary: true),
            Member(user2.Id, brandRole2.Id, ScopeType.Brand, brandId),
        });

        var perms2 = await ResolveAsync(user2, ScopeType.Store, unseededStore);
        Assert.Contains(permX2.Code, perms2);
        Assert.DoesNotContain(permY2.Code, perms2); // brand ancestor unresolved → excluded
    }

    // 3 ── expired/revoked memberships and expired/out-of-scope overrides are excluded.
    [Fact]
    public async Task expired_revoked_and_scoped_override_excluded()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();
        var now = DateTimeOffset.UtcNow;
        var storeId = Guid.NewGuid();
        var otherStore = Guid.NewGuid();

        var permActive         = Perm($"active.{t}");
        var permExpired        = Perm($"expired.{t}");
        var permRevoked        = Perm($"revoked.{t}");
        var permOverrideExpired = Perm($"ov.expired.{t}");
        var permScopedOther    = Perm($"ov.scoped.{t}");
        var permGlobalApplied  = Perm($"ov.global.{t}");

        var rActive  = RoleRow($"r_active.{t}",  ScopeType.Store);
        var rExpired = RoleRow($"r_expired.{t}", ScopeType.Store);
        var rRevoked = RoleRow($"r_revoked.{t}", ScopeType.Store);
        var user = Usr();

        await SeedAsync(new object[]
        {
            permActive, permExpired, permRevoked, permOverrideExpired, permScopedOther, permGlobalApplied,
            rActive, rExpired, rRevoked, user,
            RP(rActive.Id, permActive.Id), RP(rExpired.Id, permExpired.Id), RP(rRevoked.Id, permRevoked.Id),
            Member(user.Id, rActive.Id,  ScopeType.Store, storeId, primary: true, expires: now.AddYears(1)),
            Member(user.Id, rExpired.Id, ScopeType.Store, storeId, expires: now.AddMinutes(-5)),
            Member(user.Id, rRevoked.Id, ScopeType.Store, storeId, revoked: now.AddMinutes(-5)),
            Override(user.Id, permOverrideExpired.Id, "allow", expires: now.AddMinutes(-5)),         // expired → ignored
            Override(user.Id, permScopedOther.Id, "allow", ScopeType.Store, otherStore),             // other subtree → not applied
            Override(user.Id, permGlobalApplied.Id, "allow"),                                        // null-scope → applies
        });

        var perms = await ResolveAsync(user, ScopeType.Store, storeId);

        Assert.Contains(permActive.Code, perms);
        Assert.Contains(permGlobalApplied.Code, perms);
        Assert.DoesNotContain(permExpired.Code, perms);
        Assert.DoesNotContain(permRevoked.Code, perms);
        Assert.DoesNotContain(permOverrideExpired.Code, perms);
        Assert.DoesNotContain(permScopedOther.Code, perms);
    }

    // 4 ── step-up (§8): only high/critical effective perms are emitted; platform_admin carries the FULL
    //       high/critical catalog; scope_nodes lists every membership node.
    [Fact]
    public async Task stepup_perms_and_scope_nodes_emitted()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();
        var storeId = Guid.NewGuid();
        var brandId = Guid.NewGuid();

        var high     = Perm($"finance.settle.{t}",  RiskLevel.High);
        var critical = Perm($"royalty.void.{t}",    RiskLevel.Critical);
        var normal   = Perm($"orders.read.{t}",     RiskLevel.Normal);
        var highUnheld = Perm($"platform.wipe.{t}", RiskLevel.High); // exists but granted to nobody here

        var platformRole = RoleRow($"plat.{t}");
        var storeRole    = RoleRow($"store.{t}", ScopeType.Store);
        var brandRole    = RoleRow($"brand.{t}", ScopeType.Brand);
        var user = Usr();

        await SeedAsync(new object[]
        {
            high, critical, normal, highUnheld, platformRole, storeRole, brandRole, user,
            RP(platformRole.Id, high.Id), RP(platformRole.Id, critical.Id), RP(platformRole.Id, normal.Id),
            Member(user.Id, platformRole.Id, ScopeType.Platform, null, primary: true),
            Member(user.Id, storeRole.Id,    ScopeType.Store,    storeId),
            Member(user.Id, brandRole.Id,    ScopeType.Brand,    brandId),
        });

        await using var db = _fx.NewContext();
        var claims = await ScopeResolver.BuildTokenClaimsAsync(_fx.AsCore(db), user, enforceEntitlement: false);

        var stepUp = Set(claims.StepUpPerms ?? "");
        Assert.Contains(high.Code, stepUp);
        Assert.Contains(critical.Code, stepUp);
        Assert.DoesNotContain(normal.Code, stepUp);        // normal risk never steps up
        Assert.DoesNotContain(highUnheld.Code, stepUp);    // high but not effective → not for this user

        var nodes = Set(claims.ScopeNodes ?? "");
        Assert.Contains(ScopeType.Platform, nodes);
        Assert.Contains($"{ScopeType.Store}:{storeId}", nodes);
        Assert.Contains($"{ScopeType.Brand}:{brandId}", nodes);

        // platform_admin bypasses membership resolution but NOT step-up → FULL high/critical catalog,
        // including high perms it holds no role for.
        var admin = Usr(UserType.PlatformAdmin);
        await using var db2 = _fx.NewContext();
        var adminClaims = await ScopeResolver.BuildTokenClaimsAsync(_fx.AsCore(db2), admin, enforceEntitlement: false);
        var adminStepUp = Set(adminClaims.StepUpPerms ?? "");
        Assert.Contains(high.Code, adminStepUp);
        Assert.Contains(critical.Code, adminStepUp);
        Assert.Contains(highUnheld.Code, adminStepUp);     // full catalog, even un-granted
    }

    // 5 ── special actors: an 'auditor' role's deny on a mutating perm, and a 'franchise_owner' role's
    //       deny on royalty.override, both win over a companion role that allows them.
    [Fact]
    public async Task special_actor_auditor_and_franchise_owner_denied()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var read    = Perm($"orders.read.{t}");
        var mutate  = Perm($"orders.refund.{t}");
        var royalty = Perm($"royalty.override.{t}");

        var auditor        = RoleRow($"auditor.{t}");          // read-only: explicitly denies mutation
        var franchiseOwner = RoleRow($"franchise_owner.{t}");  // denied royalty.override by policy
        var companion      = RoleRow($"broad.{t}");            // grants everything
        var user = Usr();

        await SeedAsync(new object[]
        {
            read, mutate, royalty, auditor, franchiseOwner, companion, user,
            RP(auditor.Id, read.Id, "allow"), RP(auditor.Id, mutate.Id, "deny"),
            RP(franchiseOwner.Id, royalty.Id, "deny"),
            RP(companion.Id, read.Id, "allow"), RP(companion.Id, mutate.Id, "allow"), RP(companion.Id, royalty.Id, "allow"),
            Member(user.Id, auditor.Id,        ScopeType.Platform, null, primary: true),
            Member(user.Id, franchiseOwner.Id, ScopeType.Platform, null),
            Member(user.Id, companion.Id,      ScopeType.Platform, null),
        });

        var perms = await ResolveAsync(user);
        Assert.Contains(read.Code, perms);
        Assert.DoesNotContain(mutate.Code, perms);   // auditor deny wins
        Assert.DoesNotContain(royalty.Code, perms);  // franchise_owner deny wins
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────
    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task SeedAsync(IEnumerable<object> entities)
    {
        await using var seed = _fx.NewContext(); // NO interceptor → seeding is not audited
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    private async Task<HashSet<string>> ResolveAsync(User user, string? scopeType = null, Guid? scopeId = null)
    {
        await using var db = _fx.NewContext();
        var claims = await ScopeResolver.BuildTokenClaimsAsync(
            _fx.AsCore(db), user, scopeType, scopeId, enforceEntitlement: false);
        return Set(claims.Permissions);
    }

    private static HashSet<string> Set(string spaceJoined) =>
        spaceJoined.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Permission Perm(string code, string risk = RiskLevel.Normal) => new()
    {
        Id = Guid.NewGuid(), Code = code, Module = "test", Action = "act", Name = code,
        IsSystem = true, RequiresScope = false, RiskLevel = risk, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Role RoleRow(string code, string scopeType = ScopeType.Platform) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = code, ScopeType = scopeType,
        IsSystem = false, IsAssignable = true, Priority = 100, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static RolePermission RP(Guid roleId, Guid permissionId, string effect = "allow") => new()
    {
        Id = Guid.NewGuid(), RoleId = roleId, PermissionId = permissionId, Effect = effect,
        GrantedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static UserScopeMembership Member(
        Guid userId, Guid roleId, string scopeType, Guid? scopeId,
        bool primary = false, DateTimeOffset? expires = null, DateTimeOffset? revoked = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, RoleId = roleId, ScopeType = scopeType, ScopeId = scopeId,
        IsPrimary = primary, GrantedAt = DateTimeOffset.UtcNow, ExpiresAt = expires, RevokedAt = revoked,
        Metadata = "{}", CreatedAt = DateTimeOffset.UtcNow,
    };

    private static UserPermissionOverride Override(
        Guid userId, Guid permissionId, string effect,
        string? scopeType = null, Guid? scopeId = null, DateTimeOffset? expires = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, PermissionId = permissionId, Effect = effect,
        ScopeType = scopeType, ScopeId = scopeId, ExpiresAt = expires,
        GrantedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static User Usr(string userType = UserType.Staff) => new()
    {
        Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@t.test", UserType = userType,
        Locale = "en-IN", Timezone = "Asia/Kolkata", Status = "active", Version = 1, PermVersion = 0,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };
}
