using core.Application.Identity.AccessControl.Queries.GetPersonMemberships;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Audit finding A-4, "blind writes" — the memberships half.
///
/// <para><b>What was wrong.</b> The person drawer could GRANT and REVOKE memberships but nothing
/// could LIST them. The panel said so in its own copy: it could only offer to revoke what the
/// current browser session had just granted, so anything granted yesterday was invisible and
/// unrevokable from the UI. Write-only authority is the worst kind to leave unreadable.</para>
///
/// <para>These run against a real PostgreSQL: the query is a join across memberships, roles and
/// four scope tables with a tenancy predicate, so it is only correct if it is correct as SQL. The
/// isolation cases matter most — this endpoint returns a named person's authority, and the same
/// table it reads is the one F-1/F-2 leaked from.</para>
/// </summary>
[Collection("rbac-ef")]
public sealed class PersonMembershipsQueryTests
{
    private readonly RbacEfFixture _fx;
    public PersonMembershipsQueryTests(RbacEfFixture fx) => _fx = fx;

    [Fact]
    public async Task It_returns_live_memberships_with_their_scope_names_resolved()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        var rows = await RunAsync(w.Person, Actor(w.BrandA));

        Assert.Equal(2, rows.Count);

        // Primary first — the order the panel reads them in.
        Assert.True(rows[0].IsPrimary);
        Assert.Equal(ScopeType.Brand, rows[0].ScopeType);
        Assert.Equal("Brand A", rows[0].ScopeName);
        Assert.Equal("brand.role", rows[0].RoleCode);

        var store = rows.Single(r => r.ScopeType == ScopeType.Store);
        // Resolved server-side, so the panel can name a scope without loading every store list.
        Assert.Equal("Store One", store.ScopeName);
    }

    [Fact]
    public async Task A_revoked_membership_is_not_returned()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        var rows = await RunAsync(w.Person, Actor(w.BrandA));

        Assert.DoesNotContain(rows, r => r.RoleCode == "revoked.role");
    }

    [Fact]
    public async Task An_expired_membership_is_not_returned()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        // Liveness is the whole difference between "was once" and "is".
        var rows = await RunAsync(w.Person, Actor(w.BrandA));

        Assert.DoesNotContain(rows, r => r.RoleCode == "expired.role");
    }

    [Fact]
    public async Task A_membership_under_another_brand_is_not_returned()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        // One person may legitimately hold memberships under two brands; one brand's admin has no
        // business seeing the other's.
        var rows = await RunAsync(w.Person, Actor(w.BrandA));

        Assert.DoesNotContain(rows, r => r.ScopeId == w.BrandB);
    }

    [Fact]
    public async Task A_person_outside_the_callers_brand_yields_nothing()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        var rows = await RunAsync(w.ForeignPerson, Actor(w.BrandA));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task A_foreign_person_and_a_fabricated_id_are_indistinguishable()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        // Otherwise the endpoint is an oracle for which user ids exist, which is the shape of the
        // disclosure F-1/F-2 were.
        var foreign = await RunAsync(w.ForeignPerson, Actor(w.BrandA));
        var fabricated = await RunAsync(Guid.NewGuid(), Actor(w.BrandA));

        Assert.Empty(foreign);
        Assert.Empty(fabricated);
    }

    [Fact]
    public async Task A_platform_membership_is_shown_to_a_platform_admin_and_to_nobody_else()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        // GrantMembership only lets a platform admin create a platform-scoped membership, so
        // without this arm the one caller who can make one is the one caller who cannot see it —
        // the blind write this whole task exists to close, reintroduced.
        var asPlatform = await RunAsync(w.Person, PlatformActor(w.BrandA));
        Assert.Contains(asPlatform, r => r.ScopeType == ScopeType.Platform && r.ScopeName == "Platform");

        var asBrand = await RunAsync(w.Person, Actor(w.BrandA));
        Assert.DoesNotContain(asBrand, r => r.ScopeType == ScopeType.Platform);
    }

    // ── fixture ─────────────────────────────────────────────────────────────────────────────────

    private sealed record World(Guid BrandA, Guid BrandB, Guid Person, Guid ForeignPerson);

    private async Task<IReadOnlyList<core.Application.Identity.AccessControl.Dtos.PersonMembershipDto>>
        RunAsync(Guid personId, ICurrentUser actor)
    {
        await using var db = _fx.NewContext();
        var handler = new GetPersonMembershipsQueryHandler(_fx.AsCore(db), actor);
        return await handler.HandleAsync(new GetPersonMembershipsQuery(personId), default);
    }

    private async Task<World> SeedAsync()
    {
        var t = Guid.NewGuid().ToString("N")[..8];
        var brandA = Guid.NewGuid();
        var brandB = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var franchiseId = Guid.NewGuid();
        var platformId = Guid.NewGuid();

        await ExecAsync($"""
            INSERT INTO tenancy_org.platforms (id, code, name)
                VALUES ('{platformId}', 'P{t}', 'Platform {t}');
            INSERT INTO tenancy_org.brands (id, platform_id, name, code, status) VALUES
                ('{brandA}', '{platformId}', 'Brand A', 'A{t}', 'active'),
                ('{brandB}', '{platformId}', 'Brand B', 'B{t}', 'active');
            INSERT INTO tenancy_org.franchises
                (id, brand_id, code, legal_name, contact_phone, billing_address, status)
                VALUES ('{franchiseId}', '{brandA}', 'F{t}', 'Franchise {t}',
                        '+910000000000', jsonb_build_object(), 'active');
            INSERT INTO tenancy_org.stores
                (id, brand_id, franchise_id, code, name, address_line1, city, state, pincode, status)
                VALUES ('{storeId}', '{brandA}', '{franchiseId}', 'S{t}', 'Store One',
                        '1 Test Way', 'Mumbai', 'MH', '400001', 'active');
            """);

        var brandRole    = RoleRow($"brand.role");
        var storeRole    = RoleRow($"store.role");
        var revokedRole  = RoleRow($"revoked.role");
        var expiredRole  = RoleRow($"expired.role");
        var otherRole    = RoleRow($"other.brand.role");
        var platformRole = RoleRow($"platform.role");

        var person  = Usr();
        var foreign = Usr();

        await SeedEntitiesAsync(
        [
            brandRole, storeRole, revokedRole, expiredRole, otherRole, platformRole, person, foreign,
            Member(person.Id,  brandRole.Id,    ScopeType.Brand,    brandA,  primary: true),
            Member(person.Id,  storeRole.Id,    ScopeType.Store,    storeId),
            Member(person.Id,  revokedRole.Id,  ScopeType.Brand,    brandA,  revoked: DateTimeOffset.UtcNow.AddDays(-1)),
            Member(person.Id,  expiredRole.Id,  ScopeType.Brand,    brandA,  expires: DateTimeOffset.UtcNow.AddDays(-1)),
            Member(person.Id,  otherRole.Id,    ScopeType.Brand,    brandB),
            Member(person.Id,  platformRole.Id, ScopeType.Platform, null),
            Member(foreign.Id, otherRole.Id,    ScopeType.Brand,    brandB,  primary: true),
        ]);

        return new World(brandA, brandB, person.Id, foreign.Id);
    }

    private async Task ExecAsync(string sql)
    {
        await using var db = _fx.NewContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private async Task SeedEntitiesAsync(IEnumerable<object> entities)
    {
        await using var seed = _fx.NewContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    private static ICurrentUser Actor(Guid brandId) => new StubCurrentUser(brandId, platformAdmin: false);
    private static ICurrentUser PlatformActor(Guid brandId) => new StubCurrentUser(brandId, platformAdmin: true);

    private static Role RoleRow(string code) => new()
    {
        Id = Guid.NewGuid(), Code = $"{code}", Name = code, ScopeType = ScopeType.Brand,
        IsSystem = false, IsAssignable = true, Priority = 50, Status = "active",
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

    private sealed class StubCurrentUser(Guid? brandId, bool platformAdmin) : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;
        public string? UserType => platformAdmin
            ? laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin
            : laundryghar.SharedDataModel.Enums.UserType.Staff;
        public string? Email => null;
        public string? Phone => null;
        public Guid? BrandId => brandId;
        public Guid? FranchiseId => null;
        public Guid? StoreId => null;
        public string? ScopeType => null;
        public Guid? ScopeId => null;
        public bool IsAuthenticated => true;
        public bool IsPlatformAdmin => platformAdmin;
        public bool HasPermission(string permissionCode) => true;
        public IReadOnlyCollection<ScopeNode> ScopeNodes => [];
        public bool IsWithinScope(Guid? b = null, Guid? f = null, Guid? s = null, Guid? w = null) => true;
        public Guid? ImpersonationGrantId => null;
        public string? ImpersonationScope => null;
        public Guid? TryGetBrandId() => brandId;
        public Guid RequireBrandId() => brandId ?? throw new UnauthorizedAccessException();
    }
}
