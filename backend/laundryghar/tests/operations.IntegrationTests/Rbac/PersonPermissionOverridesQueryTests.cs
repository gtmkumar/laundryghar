using core.Application.Identity.AccessControl.Queries.GetPersonPermissionOverrides;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Audit finding A-4, "blind writes" — the permission-override half, which the ABAC plan had also
/// named independently as task A8.2: "<c>user_permission_override</c> is write-only with no query to
/// read it back".
///
/// <para>An override is the sharpest instrument in the authorization model — a single allow or deny
/// layered on one person, where deny beats every role grant — so being unable to read one back means
/// nobody can answer "what exceptions does this person carry" without opening the database.</para>
///
/// <para>Real PostgreSQL: the query joins overrides to permissions and to four scope tables under a
/// tenancy predicate, so it is only correct if it is correct as SQL.</para>
/// </summary>
[Collection("rbac-ef")]
public sealed class PersonPermissionOverridesQueryTests
{
    private readonly RbacEfFixture _fx;
    public PersonPermissionOverridesQueryTests(RbacEfFixture fx) => _fx = fx;

    [Fact]
    public async Task It_returns_live_overrides_with_permission_and_scope_details()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        var rows = await RunAsync(w.Person, Actor(w.BrandA));

        Assert.Equal(2, rows.Count);

        // Deny first: it is the stronger statement and the one an admin most needs to notice.
        Assert.Equal("deny", rows[0].Effect);

        var scoped = rows.Single(r => r.ScopeType == ScopeType.Franchise);
        Assert.Equal("Franchise One", scoped.ScopeName);   // resolved server-side
        Assert.Equal("allow", scoped.Effect);
        Assert.Equal("a scoped grant", scoped.Reason);
        Assert.NotNull(scoped.ExpiresAt);

        var global = rows.Single(r => r.ScopeType == null);
        Assert.Null(global.ScopeName);                      // global overrides name no node
        Assert.False(string.IsNullOrWhiteSpace(global.PermissionName));
        Assert.False(string.IsNullOrWhiteSpace(global.Module));
    }

    [Fact]
    public async Task An_expired_override_is_not_returned()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        // Permission resolution already ignores an expired row, so listing one would report
        // authority the person does not actually have.
        var rows = await RunAsync(w.Person, Actor(w.BrandA));

        Assert.DoesNotContain(rows, r => r.PermissionCode == w.ExpiredCode);
    }

    [Fact]
    public async Task A_person_outside_the_callers_brand_yields_nothing()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        Assert.Empty(await RunAsync(w.ForeignPerson, Actor(w.BrandA)));
    }

    [Fact]
    public async Task A_foreign_person_and_a_fabricated_id_are_indistinguishable()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        // The endpoint reports a named person's exceptions; it must not double as an oracle for
        // which user ids exist.
        Assert.Empty(await RunAsync(w.ForeignPerson, Actor(w.BrandA)));
        Assert.Empty(await RunAsync(Guid.NewGuid(), Actor(w.BrandA)));
    }

    [Fact]
    public async Task A_platform_admin_with_no_brand_selected_is_not_filtered()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        // Mirrors UserBrandScope's own degradation: cross-tenant visibility is the operator's job
        // until they select a brand.
        Assert.NotEmpty(await RunAsync(w.ForeignPerson, Actor(null)));
    }

    // ── fixture ─────────────────────────────────────────────────────────────────────────────────

    private sealed record World(Guid BrandA, Guid Person, Guid ForeignPerson, string ExpiredCode);

    private async Task<IReadOnlyList<core.Application.Identity.AccessControl.Dtos.PersonPermissionOverrideDto>>
        RunAsync(Guid personId, ICurrentUser actor)
    {
        await using var db = _fx.NewContext();
        var handler = new GetPersonPermissionOverridesQueryHandler(_fx.AsCore(db), actor);
        return await handler.HandleAsync(new GetPersonPermissionOverridesQuery(personId), default);
    }

    private async Task<World> SeedAsync()
    {
        var t = Guid.NewGuid().ToString("N")[..8];
        var brandA = Guid.NewGuid();
        var brandB = Guid.NewGuid();
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
                VALUES ('{franchiseId}', '{brandA}', 'F{t}', 'Franchise One',
                        '+910000000000', jsonb_build_object(), 'active');
            """);

        var denyPerm    = Perm($"t4.deny.{t}");
        var allowPerm   = Perm($"t4.allow.{t}");
        var expiredPerm = Perm($"t4.expired.{t}");

        var role    = RoleRow($"role.{t}");
        var person  = Usr();
        var foreign = Usr();

        await SeedEntitiesAsync(
        [
            denyPerm, allowPerm, expiredPerm, role, person, foreign,
            Member(person.Id,  role.Id, ScopeType.Brand, brandA, primary: true),
            Member(foreign.Id, role.Id, ScopeType.Brand, brandB, primary: true),
            Override(person.Id,  denyPerm.Id,    "deny"),
            Override(person.Id,  allowPerm.Id,   "allow", ScopeType.Franchise, franchiseId,
                     reason: "a scoped grant", expires: DateTimeOffset.UtcNow.AddYears(1)),
            Override(person.Id,  expiredPerm.Id, "allow", expires: DateTimeOffset.UtcNow.AddDays(-1)),
            Override(foreign.Id, denyPerm.Id,    "deny"),
        ]);

        return new World(brandA, person.Id, foreign.Id, expiredPerm.Code);
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

    private static ICurrentUser Actor(Guid? brandId) => new StubCurrentUser(brandId);

    private static Permission Perm(string code) => new()
    {
        Id = Guid.NewGuid(), Code = code, Module = code.Split('.')[0], Action = "test",
        Name = $"Permission {code}", IsSystem = true, RequiresScope = false,
        RiskLevel = "normal", Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static UserPermissionOverride Override(
        Guid userId, Guid permissionId, string effect,
        string? scopeType = null, Guid? scopeId = null,
        string? reason = null, DateTimeOffset? expires = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, PermissionId = permissionId, Effect = effect,
        ScopeType = scopeType, ScopeId = scopeId, Reason = reason, ExpiresAt = expires,
        GrantedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Role RoleRow(string code) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = code, ScopeType = ScopeType.Brand,
        IsSystem = false, IsAssignable = true, Priority = 50, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static UserScopeMembership Member(
        Guid userId, Guid roleId, string scopeType, Guid? scopeId, bool primary = false) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, RoleId = roleId, ScopeType = scopeType,
        ScopeId = scopeId, IsPrimary = primary, GrantedAt = DateTimeOffset.UtcNow,
        Metadata = "{}", CreatedAt = DateTimeOffset.UtcNow,
    };

    private static User Usr() => new()
    {
        Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@t.test", UserType = UserType.Staff,
        Locale = "en-IN", Timezone = "Asia/Kolkata", Status = "active", Version = 1, PermVersion = 0,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

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
