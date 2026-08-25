using core.Application.Identity.AccessControl.Queries.GetNavigator;
using core.Application.Identity.Auth.Common;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// EF-faithful lock-in of the PaaS entitlement axis — effective access = entitlement ∩ authorization
/// (docs/rbac-entitlement-plan.md; PLATFORM_STRATEGY.md §5). Two enforcement points, both switched by
/// the <c>Entitlement:Enforced</c> configuration key:
///
///   1. <see cref="ScopeResolver"/> — bakes the filtered permission set into the token at mint, so every
///      downstream endpoint enforces entitlement via HasPermission with no hot-path change.
///   2. <see cref="GetNavigatorQueryHandler"/> — hides un-entitled modules from the sidebar.
///
/// Until this file existed the filter had NO coverage: all three ScopeResolverTests call sites pass
/// <c>enforceEntitlement: false</c>, so the branch could regress silently. These tests run against a
/// REAL Postgres with the real <c>brand_module_entitlement.sql</c> applied.
///
/// The invariants under test, in the filter's own terms:
///   • a permission whose canonical owning module the brand has not licensed is DROPPED;
///   • the same permission SURVIVES once the brand licenses that module;
///   • a <c>is_core</c> module is always entitled — a brand can never lock its own admins out;
///   • an expired (<c>valid_until</c> in the past) or disabled licence does not entitle;
///   • an orphan permission (<c>module_key IS NULL</c>) is always kept — never fail closed on unmapped;
///   • a platform admin is exempt (cross-brand operator);
///   • with enforcement off, nothing is filtered.
/// </summary>
[Collection("rbac-ef")]
public sealed class EntitlementEnforcementTests
{
    private readonly RbacEfFixture _fx;
    public EntitlementEnforcementTests(RbacEfFixture fx) => _fx = fx;

    // ── ScopeResolver: the token-mint filter ────────────────────────────────────────────────────

    // 1 ── the core case: unlicensed → stripped, core → kept, orphan → kept.
    [Fact]
    public async Task unlicensed_module_permission_is_stripped_core_and_orphan_survive()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var unlicensed = Mod($"m_unlicensed_{t}");
        var licensed   = Mod($"m_licensed_{t}");
        var core       = Mod($"m_core_{t}", isCore: true);

        var pUnlicensed = Perm($"unlicensed.view.{t}", unlicensed.Key);
        var pLicensed   = Perm($"licensed.view.{t}",   licensed.Key);
        var pCore       = Perm($"core.view.{t}",       core.Key);
        var pOrphan     = Perm($"orphan.view.{t}",     moduleKey: null);

        var role = RoleRow($"role_{t}", ScopeType.Brand);
        var user = Usr();

        await SeedAsync(new object[]
        {
            unlicensed, licensed, core,
            pUnlicensed, pLicensed, pCore, pOrphan, role, user,
            RP(role.Id, pUnlicensed.Id), RP(role.Id, pLicensed.Id),
            RP(role.Id, pCore.Id),       RP(role.Id, pOrphan.Id),
            Member(user.Id, role.Id, ScopeType.Brand, brandId, primary: true),
            // The brand licenses exactly ONE of the two non-core modules.
            Licence(brandId, licensed.Key),
        });

        var perms = await ResolveAsync(user, ScopeType.Brand, brandId, enforce: true);

        Assert.DoesNotContain(pUnlicensed.Code, perms); // not licensed → dropped
        Assert.Contains(pLicensed.Code, perms);         // licensed     → kept
        Assert.Contains(pCore.Code, perms);             // is_core      → always entitled
        Assert.Contains(pOrphan.Code, perms);           // module_key NULL → never fail closed
    }

    // 2 ── a licence that has lapsed (valid_until in the past) or been disabled does not entitle.
    [Fact]
    public async Task expired_or_disabled_licence_does_not_entitle()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var expiredMod  = Mod($"m_expired_{t}");
        var disabledMod = Mod($"m_disabled_{t}");
        var validMod    = Mod($"m_valid_{t}");

        var pExpired  = Perm($"expired.view.{t}",  expiredMod.Key);
        var pDisabled = Perm($"disabled.view.{t}", disabledMod.Key);
        var pValid    = Perm($"valid.view.{t}",    validMod.Key);

        var role = RoleRow($"role_{t}", ScopeType.Brand);
        var user = Usr();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await SeedAsync(new object[]
        {
            expiredMod, disabledMod, validMod,
            pExpired, pDisabled, pValid, role, user,
            RP(role.Id, pExpired.Id), RP(role.Id, pDisabled.Id), RP(role.Id, pValid.Id),
            Member(user.Id, role.Id, ScopeType.Brand, brandId, primary: true),
            Licence(brandId, expiredMod.Key,  validUntil: today.AddDays(-1)),
            Licence(brandId, disabledMod.Key, enabled: false),
            // Boundary: valid_until == today still entitles (the filter tests `>= today`).
            Licence(brandId, validMod.Key,    validUntil: today),
        });

        var perms = await ResolveAsync(user, ScopeType.Brand, brandId, enforce: true);

        Assert.DoesNotContain(pExpired.Code, perms);
        Assert.DoesNotContain(pDisabled.Code, perms);
        Assert.Contains(pValid.Code, perms); // expires today → still valid today
    }

    // 3 ── a platform admin is a cross-brand operator and is exempt from the brand's licensing.
    [Fact]
    public async Task platform_admin_is_exempt_from_the_entitlement_filter()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var unlicensed = Mod($"m_pa_{t}");
        var perm = Perm($"pa.view.{t}", unlicensed.Key);
        var role = RoleRow($"role_{t}", ScopeType.Brand);

        var staff = Usr();                                   // ordinary staff → filtered
        var admin = Usr(UserType.PlatformAdmin);             // platform admin → exempt

        await SeedAsync(new object[]
        {
            unlicensed, perm, role, staff, admin,
            RP(role.Id, perm.Id),
            Member(staff.Id, role.Id, ScopeType.Brand, brandId, primary: true),
            Member(admin.Id, role.Id, ScopeType.Brand, brandId, primary: true),
            // deliberately NO licence for `unlicensed`
        });

        var staffPerms = await ResolveAsync(staff, ScopeType.Brand, brandId, enforce: true);
        var adminPerms = await ResolveAsync(admin, ScopeType.Brand, brandId, enforce: true);

        Assert.DoesNotContain(perm.Code, staffPerms); // brand-scoped staff is filtered
        Assert.Contains(perm.Code, adminPerms);       // platform admin is not
    }

    // 4 ── the switch itself: with enforcement OFF the same unlicensed permission survives. This is
    //      the regression guard for the flag, and documents today's shipped default.
    [Fact]
    public async Task enforcement_off_keeps_unlicensed_permissions()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var unlicensed = Mod($"m_off_{t}");
        var perm = Perm($"off.view.{t}", unlicensed.Key);
        var role = RoleRow($"role_{t}", ScopeType.Brand);
        var user = Usr();

        await SeedAsync(new object[]
        {
            unlicensed, perm, role, user,
            RP(role.Id, perm.Id),
            Member(user.Id, role.Id, ScopeType.Brand, brandId, primary: true),
        });

        var enforced = await ResolveAsync(user, ScopeType.Brand, brandId, enforce: true);
        var relaxed  = await ResolveAsync(user, ScopeType.Brand, brandId, enforce: false);

        Assert.DoesNotContain(perm.Code, enforced);
        Assert.Contains(perm.Code, relaxed);
    }

    // ── GetNavigator: the sidebar filter ────────────────────────────────────────────────────────

    // 5 ── an un-entitled module is hidden from the navigator even when the user HOLDS its required
    //      permission — proving it is the entitlement gate doing the hiding, not the permission gate.
    [Fact]
    public async Task navigator_hides_unentitled_modules_but_never_core()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var unlicensed = Mod($"nav_unlicensed_{t}", showInNav: true, requiredPermission: $"nav.view.{t}");
        var licensed   = Mod($"nav_licensed_{t}",   showInNav: true, requiredPermission: $"nav.view.{t}");
        var core       = Mod($"nav_core_{t}",       showInNav: true, requiredPermission: $"nav.view.{t}", isCore: true);

        await SeedAsync(new object[] { unlicensed, licensed, core, Licence(brandId, licensed.Key) });

        // The caller holds the required permission for all three — only entitlement can differ.
        var user = new FakeCurrentUser
        {
            UserId = Guid.NewGuid(),
            BrandId = brandId,
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"nav.view.{t}" },
        };

        var keys = await NavigateAsync(user, enforced: true);

        Assert.DoesNotContain(unlicensed.Key, keys);
        Assert.Contains(licensed.Key, keys);
        Assert.Contains(core.Key, keys); // is_core bypasses entitlement
    }

    // 6 ── with enforcement off the navigator shows the un-entitled module again.
    [Fact]
    public async Task navigator_shows_unentitled_modules_when_enforcement_off()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var brandId = Guid.NewGuid();
        await _fx.SeedBrandAsync(brandId);

        var unlicensed = Mod($"navoff_{t}", showInNav: true, requiredPermission: $"navoff.view.{t}");
        await SeedAsync(new object[] { unlicensed });

        var user = new FakeCurrentUser
        {
            UserId = Guid.NewGuid(),
            BrandId = brandId,
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"navoff.view.{t}" },
        };

        Assert.DoesNotContain(unlicensed.Key, await NavigateAsync(user, enforced: true));
        Assert.Contains(unlicensed.Key,       await NavigateAsync(user, enforced: false));
    }

    // 7 ── no brand context (platform admin with nothing selected) → entitlement is not applied at all;
    //      the full catalogue is offered, still gated by permissions.
    [Fact]
    public async Task navigator_without_brand_context_applies_no_entitlement()
    {
        if (!_fx.DockerAvailable) return;
        var t = Tag();

        var unlicensed = Mod($"navnb_{t}", showInNav: true, requiredPermission: $"navnb.view.{t}");
        await SeedAsync(new object[] { unlicensed });

        var admin = new FakeCurrentUser { UserId = Guid.NewGuid(), IsPlatformAdmin = true }; // BrandId null

        Assert.Contains(unlicensed.Key, await NavigateAsync(admin, enforced: true));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Seeds in two passes: <see cref="AppModule"/> rows first, everything else second.
    /// <see cref="BrandModule"/> carries no navigation to <see cref="AppModule"/> (the FK is on the
    /// natural key <c>module_key</c>), so EF cannot infer the dependency and would otherwise order the
    /// brand_module INSERT ahead of its parent module, tripping brand_module_module_key_fkey. Two
    /// SaveChanges calls make the order explicit. NO interceptor → seeding is not audited.</summary>
    private async Task SeedAsync(IEnumerable<object> entities)
    {
        var all = entities.ToList();
        await using var seed = _fx.NewContext();

        var modules = all.OfType<AppModule>().Cast<object>().ToList();
        if (modules.Count > 0)
        {
            seed.AddRange(modules);
            await seed.SaveChangesAsync();
        }

        var rest = all.Where(e => e is not AppModule).ToList();
        if (rest.Count > 0)
        {
            seed.AddRange(rest);
            await seed.SaveChangesAsync();
        }
    }

    private async Task<HashSet<string>> ResolveAsync(User user, string scopeType, Guid scopeId, bool enforce)
    {
        await using var db = _fx.NewContext();
        var claims = await ScopeResolver.BuildTokenClaimsAsync(
            _fx.AsCore(db), user, scopeType, scopeId, enforceEntitlement: enforce);
        return claims.Permissions
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HashSet<string>> NavigateAsync(FakeCurrentUser user, bool enforced)
    {
        await using var db = _fx.NewContext();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entitlement:Enforced"] = enforced ? "true" : "false",
            })
            .Build();

        var handler = new GetNavigatorQueryHandler(_fx.AsCore(db), user, config);
        var nav = await handler.HandleAsync(new GetNavigatorQuery(), CancellationToken.None);

        return nav.Sections
            .SelectMany(s => s.Items)
            .Select(i => i.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static AppModule Mod(
        string key, bool isCore = false, bool showInNav = false, string? requiredPermission = null) => new()
    {
        Id = Guid.NewGuid(), Key = key, Label = key, NavOrder = 100, MatrixOrder = 100,
        ShowInNav = showInNav, ShowInMatrix = true, RequiredPermission = requiredPermission,
        PermissionModules = [], IsCore = isCore, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static BrandModule Licence(
        Guid brandId, string moduleKey, bool enabled = true, DateOnly? validUntil = null) => new()
    {
        BrandId = brandId, ModuleKey = moduleKey, Enabled = enabled, ValidUntil = validUntil,
        Source = "manual", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Permission Perm(string code, string? moduleKey) => new()
    {
        Id = Guid.NewGuid(), Code = code, Module = "test", Action = "act", Name = code,
        ModuleKey = moduleKey, IsSystem = true, RequiresScope = false,
        RiskLevel = RiskLevel.Normal, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Role RoleRow(string code, string scopeType) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = code, ScopeType = scopeType,
        IsSystem = false, IsAssignable = true, Priority = 100, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static RolePermission RP(Guid roleId, Guid permissionId) => new()
    {
        Id = Guid.NewGuid(), RoleId = roleId, PermissionId = permissionId, Effect = "allow",
        GrantedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static UserScopeMembership Member(
        Guid userId, Guid roleId, string scopeType, Guid? scopeId, bool primary = false) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, RoleId = roleId, ScopeType = scopeType, ScopeId = scopeId,
        IsPrimary = primary, GrantedAt = DateTimeOffset.UtcNow, Metadata = "{}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static User Usr(string userType = UserType.Staff) => new()
    {
        Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@t.test", UserType = userType,
        Locale = "en-IN", Timezone = "Asia/Kolkata", Status = "active", Version = 1, PermVersion = 0,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };
}
