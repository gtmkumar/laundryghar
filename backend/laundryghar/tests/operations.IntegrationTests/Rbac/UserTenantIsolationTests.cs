using core.Application.Identity.Users.Common;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Regression lock for a cross-tenant PII disclosure found by live audit.
///
/// <para><b>What was broken.</b> <c>GetUsersQueryHandler</c> did not inject <c>ICurrentUser</c> at
/// all, and <c>GetUserByIdQueryHandler</c> filtered only on the id. Against the running system, a
/// brand admin listed <b>every user on the platform</b> — 20 accounts across 12 unrelated brands,
/// byte-identical for two different tenants' admins — and could read any of them by id, returning
/// email, phone, employment details, PAN, masked Aadhaar, bank account, IFSC and UPI. The financial
/// mask was no protection: <c>brand_admin</c> holds <c>users.read_financial</c>.</para>
///
/// <para><b>Why no other layer caught it.</b> <c>identity_access.users</c> has no <c>brand_id</c>
/// column and RLS is disabled on it — confirmed against the live database — so a tenant's identity
/// boundary on this table exists ONLY in application code. There is no second line of defence here,
/// which is exactly why it needs a test.</para>
///
/// <para>These run against a real PostgreSQL through the shared RBAC fixture, so the predicate is
/// exercised as SQL rather than as LINQ-to-objects — the in-memory version of this filter would
/// pass while paginating wrongly.</para>
/// </summary>
[Collection("rbac-ef")]
public sealed class UserTenantIsolationTests
{
    private readonly RbacEfFixture _fx;
    public UserTenantIsolationTests(RbacEfFixture fx) => _fx = fx;

    [Fact]
    public async Task A_brand_scoped_caller_sees_only_its_own_brands_users()
    {
        if (!_fx.DockerAvailable) return;

        var (brandA, brandB, aUser, bUser, _) = await SeedTwoBrandsAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);

        var seenByA = await core.Users.AsNoTracking()
            .ScopedToCallerBrand(core, Actor(brandA))
            .Select(u => u.Id).ToListAsync();

        Assert.Contains(aUser, seenByA);
        Assert.DoesNotContain(bUser, seenByA);   // the disclosure, locked shut

        var seenByB = await core.Users.AsNoTracking()
            .ScopedToCallerBrand(core, Actor(brandB))
            .Select(u => u.Id).ToListAsync();

        Assert.Contains(bUser, seenByB);
        Assert.DoesNotContain(aUser, seenByB);
    }

    [Fact]
    public async Task Reaching_a_foreign_user_by_explicit_id_finds_nothing()
    {
        if (!_fx.DockerAvailable) return;

        var (brandA, _, _, bUser, _) = await SeedTwoBrandsAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);

        // The by-id path: knowing the id must not be enough. This is the shape the audit exploited.
        var found = await core.Users.AsNoTracking()
            .ScopedToCallerBrand(core, Actor(brandA))
            .FirstOrDefaultAsync(u => u.Id == bUser);

        Assert.Null(found);
    }

    [Fact]
    public async Task Staff_scoped_below_the_brand_still_belong_to_it()
    {
        if (!_fx.DockerAvailable) return;

        var (brandA, _, _, _, storeUser) = await SeedTwoBrandsAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);

        // A store-scoped user's tenant is resolved THROUGH the store row, not read off the
        // membership. Miss this and every franchise/store/warehouse employee vanishes from their
        // own brand's directory — a fix that broke the screen would be its own bug.
        var seen = await core.Users.AsNoTracking()
            .ScopedToCallerBrand(core, Actor(brandA))
            .Select(u => u.Id).ToListAsync();

        Assert.Contains(storeUser, seen);
    }

    [Fact]
    public async Task A_revoked_membership_no_longer_places_a_user_in_the_brand()
    {
        if (!_fx.DockerAvailable) return;

        var (brandId, leaver) = await SeedOneBrandUserAsync(
            revoked: DateTimeOffset.UtcNow.AddDays(-1));

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);

        var seen = await core.Users.AsNoTracking()
            .ScopedToCallerBrand(core, Actor(brandId))
            .Select(u => u.Id).ToListAsync();

        Assert.DoesNotContain(leaver, seen);
    }

    [Fact]
    public async Task An_expired_membership_no_longer_places_a_user_in_the_brand()
    {
        if (!_fx.DockerAvailable) return;

        var (brandId, expired) = await SeedOneBrandUserAsync(
            expires: DateTimeOffset.UtcNow.AddDays(-1));

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);

        var seen = await core.Users.AsNoTracking()
            .ScopedToCallerBrand(core, Actor(brandId))
            .Select(u => u.Id).ToListAsync();

        Assert.DoesNotContain(expired, seen);
    }

    [Fact]
    public async Task A_platform_admin_with_no_brand_selected_is_not_filtered()
    {
        if (!_fx.DockerAvailable) return;

        var (_, _, aUser, bUser, _) = await SeedTwoBrandsAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);

        // Cross-tenant visibility is the platform operator's job. The moment they select a brand
        // via X-Brand-Id, TryGetBrandId returns it and they are scoped like everyone else.
        var seen = await core.Users.AsNoTracking()
            .ScopedToCallerBrand(core, Actor(brandId: null))
            .Select(u => u.Id).ToListAsync();

        Assert.Contains(aUser, seen);
        Assert.Contains(bUser, seen);
    }

    // ── fixture ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Two brands, one brand-scoped user each, plus a store-scoped user under brand A.</summary>
    private async Task<(Guid BrandA, Guid BrandB, Guid AUser, Guid BUser, Guid StoreUser)>
        SeedTwoBrandsAsync()
    {
        var t = Tag();
        var brandAId = Guid.NewGuid();
        var brandBId = Guid.NewGuid();
        var franchiseId = Guid.NewGuid();
        var storeId = Guid.NewGuid();

        // Tenancy rows go in as raw SQL, like the sibling fixtures: the EF entities require columns
        // the real tables supply defaults for (currency_code and friends), so seeding them through
        // the model fails on constraints that production never hits.
        var platformId = Guid.NewGuid();
        await ExecAsync($"""
            INSERT INTO tenancy_org.platforms (id, code, name)
                VALUES ('{platformId}', 'P{t}', 'Audit platform {t}');
            INSERT INTO tenancy_org.brands (id, platform_id, name, code, status) VALUES
                ('{brandAId}', '{platformId}', 'Audit brand A {t}', 'A{t}', 'active'),
                ('{brandBId}', '{platformId}', 'Audit brand B {t}', 'B{t}', 'active');
            INSERT INTO tenancy_org.franchises
                (id, brand_id, code, legal_name, contact_phone, billing_address, status)
                VALUES ('{franchiseId}', '{brandAId}', 'F{t}', 'Franchise {t}',
                        '+910000000000', jsonb_build_object(), 'active');
            INSERT INTO tenancy_org.stores
                (id, brand_id, franchise_id, code, name, address_line1, city, state, pincode, status)
                VALUES ('{storeId}', '{brandAId}', '{franchiseId}', 'S{t}', 'Store {t}',
                        '1 Test Way', 'Mumbai', 'MH', '400001', 'active');
            """);

        var role = RoleRow($"role.{t}");
        var aUser = Usr();
        var bUser = Usr();
        var storeUser = Usr();

        await SeedAsync(new object[]
        {
            role, aUser, bUser, storeUser,
            Member(aUser.Id,     role.Id, ScopeType.Brand, brandAId, primary: true),
            Member(bUser.Id,     role.Id, ScopeType.Brand, brandBId, primary: true),
            Member(storeUser.Id, role.Id, ScopeType.Store, storeId,  primary: true),
        });

        return (brandAId, brandBId, aUser.Id, bUser.Id, storeUser.Id);
    }

    /// <summary>One brand, with a single membership shaped by the caller (revoked / expired / live).</summary>
    private async Task<(Guid BrandId, Guid UserId)> SeedOneBrandUserAsync(
        DateTimeOffset? expires = null, DateTimeOffset? revoked = null)
    {
        var t = Tag();
        var brandId = Guid.NewGuid();

        var platformId = Guid.NewGuid();
        await ExecAsync($"""
            INSERT INTO tenancy_org.platforms (id, code, name)
                VALUES ('{platformId}', 'P{t}', 'Audit platform {t}');
            INSERT INTO tenancy_org.brands (id, platform_id, name, code, status)
                VALUES ('{brandId}', '{platformId}', 'Audit brand {t}', 'X{t}', 'active');
            """);

        var role = RoleRow($"role.{t}");
        var user = Usr();

        await SeedAsync(new object[]
        {
            role, user,
            Member(user.Id, role.Id, ScopeType.Brand, brandId, expires: expires, revoked: revoked),
        });

        return (brandId, user.Id);
    }

    private async Task ExecAsync(string sql)
    {
        await using var db = _fx.NewContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task SeedAsync(IEnumerable<object> entities)
    {
        await using var seed = _fx.NewContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static ICurrentUser Actor(Guid? brandId) => new StubCurrentUser(brandId);

    private static Role RoleRow(string code) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = code, ScopeType = ScopeType.Brand,
        IsSystem = false, IsAssignable = true, Priority = 100, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static UserScopeMembership Member(
        Guid userId, Guid roleId, string scopeType, Guid? scopeId,
        bool primary = false, DateTimeOffset? expires = null, DateTimeOffset? revoked = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, RoleId = roleId, ScopeType = scopeType,
        ScopeId = scopeId, IsPrimary = primary, GrantedAt = DateTimeOffset.UtcNow,
        ExpiresAt = expires, RevokedAt = revoked, Metadata = "{}", CreatedAt = DateTimeOffset.UtcNow,
    };

    private static User Usr() => new()
    {
        Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@t.test", UserType = UserType.Staff,
        Locale = "en-IN", Timezone = "Asia/Kolkata", Status = "active", Version = 1, PermVersion = 0,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Only <see cref="ICurrentUser.TryGetBrandId"/> matters to the predicate under test.</summary>
    private sealed class StubCurrentUser(Guid? brandId) : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;
        public string? UserType => laundryghar.SharedDataModel.Enums.UserType.Staff;
        public string? Email => null;
        public string? Phone => null;
        public Guid? BrandId => brandId;
        public Guid? FranchiseId => null;
        public Guid? StoreId => null;
        public string? ScopeType => null;
        public Guid? ScopeId => null;
        public bool IsAuthenticated => true;
        public bool IsPlatformAdmin => brandId is null;
        public bool HasPermission(string permissionCode) => true;
        public IReadOnlyCollection<ScopeNode> ScopeNodes => [];
        public bool IsWithinScope(Guid? b = null, Guid? f = null, Guid? s = null, Guid? w = null) => true;
        public Guid? ImpersonationGrantId => null;
        public string? ImpersonationScope => null;
        public Guid? TryGetBrandId() => brandId;
        public Guid RequireBrandId() => brandId ?? throw new UnauthorizedAccessException();
    }
}
