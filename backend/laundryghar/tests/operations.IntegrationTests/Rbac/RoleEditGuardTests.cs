using core.Application.Common.Interfaces;
using core.Application.Identity.AccessControl.Commands.SetRoleCells;
using core.Application.Identity.AccessControl.Dtos;
using core.Application.Identity.Common;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Regression lock for audit finding A-1, "guard asymmetry".
///
/// <para><b>What was broken.</b> Two endpoints write <c>identity_access.role_permissions</c> and both
/// sit behind the same <c>permissions.assign</c> policy. <c>AssignPermission</c> refused to touch a
/// system role, a role belonging to another brand, or a role outranking the caller.
/// <c>SetRoleCells</c> — the matrix save, which flips <i>many</i> permission codes per cell —
/// enforced none of those. The narrow door was locked and the wide one was not: a brand admin could
/// not grant themselves a permission one checkbox at a time, but could do it by saving a cell on
/// <c>platform_admin</c>.</para>
///
/// <para><b>Why it needs a test at this layer.</b> The rule is a join across roles and live
/// memberships, so it is only true if it is true in SQL. These run against a real PostgreSQL through
/// the shared RBAC fixture rather than against LINQ-to-objects.</para>
///
/// <para>The suite is deliberately half negative controls: a guard that refuses everything would
/// pass every "is it denied" test and break the product.</para>
/// </summary>
[Collection("rbac-ef")]
public sealed class RoleEditGuardTests
{
    private readonly RbacEfFixture _fx;
    public RoleEditGuardTests(RbacEfFixture fx) => _fx = fx;

    // ── The three guards, each refusing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_brand_admin_may_not_edit_a_system_role()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);
        var role = (await RoleEditGuard.FindLiveRoleAsync(core, w.SystemRole, default))!;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            RoleEditGuard.EnsureMayEditAsync(core, Actor(w.BrandA), w.AdminA, role, default));
    }

    [Fact]
    public async Task A_brand_admin_may_not_edit_another_brands_role()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);
        var role = (await RoleEditGuard.FindLiveRoleAsync(core, w.PeerRoleB, default))!;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            RoleEditGuard.EnsureMayEditAsync(core, Actor(w.BrandA), w.AdminA, role, default));
    }

    [Fact]
    public async Task A_brand_admin_may_not_edit_a_role_that_outranks_them()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);
        // Priority 20 against an actor whose own membership is priority 30 — lower number = higher rank.
        var role = (await RoleEditGuard.FindLiveRoleAsync(core, w.SeniorRoleA, default))!;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            RoleEditGuard.EnsureMayEditAsync(core, Actor(w.BrandA), w.AdminA, role, default));
    }

    [Fact]
    public async Task An_actor_with_no_live_membership_may_edit_nothing()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);
        // Own brand, own rank — refused solely because the actor holds no live membership to
        // outrank it with. SQL MIN over no rows is NULL, which must fall to most-restrictive.
        var role = (await RoleEditGuard.FindLiveRoleAsync(core, w.PeerRoleA, default))!;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            RoleEditGuard.EnsureMayEditAsync(core, Actor(w.BrandA), w.Stranger, role, default));
    }

    [Fact]
    public async Task A_revoked_membership_does_not_confer_rank()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);
        var role = (await RoleEditGuard.FindLiveRoleAsync(core, w.PeerRoleA, default))!;

        // AdminRevoked's only membership is revoked, so it must not count toward their rank.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            RoleEditGuard.EnsureMayEditAsync(core, Actor(w.BrandA), w.AdminRevoked, role, default));
    }

    // ── Negative controls: the guard must not refuse everything ─────────────────────────────────

    [Fact]
    public async Task A_brand_admin_may_edit_an_own_brand_role_of_equal_rank()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);
        var role = (await RoleEditGuard.FindLiveRoleAsync(core, w.PeerRoleA, default))!;

        // Equal rank passes — the check is strict `<`, matching GrantMembership. Without this
        // control a brand admin could not manage their own peer roles and the product would break.
        await RoleEditGuard.EnsureMayEditAsync(core, Actor(w.BrandA), w.AdminA, role, default);
    }

    [Fact]
    public async Task A_brand_admin_may_edit_a_junior_own_brand_role()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);
        var role = (await RoleEditGuard.FindLiveRoleAsync(core, w.JuniorRoleA, default))!;

        await RoleEditGuard.EnsureMayEditAsync(core, Actor(w.BrandA), w.AdminA, role, default);
    }

    [Fact]
    public async Task A_platform_admin_may_edit_a_system_role()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);
        var role = (await RoleEditGuard.FindLiveRoleAsync(core, w.SystemRole, default))!;

        // The platform operator's bypass is the existing model; the A-1 fix must not narrow it.
        await RoleEditGuard.EnsureMayEditAsync(core, PlatformActor(), w.AdminA, role, default);
    }

    // ── The escalation itself, driven through the handler that carried it ───────────────────────

    [Fact]
    public async Task SetRoleCells_refuses_the_escalation_and_writes_nothing()
    {
        if (!_fx.DockerAvailable) return;
        var w = await SeedAsync();

        await using var db = _fx.NewContext();
        var core = _fx.AsCore(db);

        var before = await core.RolePermissions.CountAsync(rp => rp.RoleId == w.SystemRole);

        var handler = new SetRoleCellsCommandHandler(core, Actor(w.BrandA));
        var cmd = new SetRoleCellsCommand(
            w.SystemRole,
            new SetRoleCellsRequest([new RoleCellChange("users.approve", true)]),
            w.AdminA);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.HandleAsync(cmd, default));

        // The guard runs before any cell is resolved, so a refused save must leave the table alone.
        await using var check = _fx.NewContext();
        var after = await _fx.AsCore(check).RolePermissions.CountAsync(rp => rp.RoleId == w.SystemRole);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task SetRoleCells_still_returns_not_found_for_a_missing_role()
    {
        if (!_fx.DockerAvailable) return;
        _ = await SeedAsync();

        await using var db = _fx.NewContext();
        var handler = new SetRoleCellsCommandHandler(_fx.AsCore(db), PlatformActor());

        // A role that does not exist must stay a 404 (handler returns false), not become a 403 —
        // the guard is an addition to this handler, not a change to how absence is reported.
        var ok = await handler.HandleAsync(
            new SetRoleCellsCommand(
                Guid.NewGuid(),
                new SetRoleCellsRequest([new RoleCellChange("users.approve", true)]),
                Guid.NewGuid()),
            default);

        Assert.False(ok);
    }

    // ── fixture ─────────────────────────────────────────────────────────────────────────────────

    private sealed record World(
        Guid BrandA, Guid BrandB,
        Guid SystemRole, Guid SeniorRoleA, Guid PeerRoleA, Guid JuniorRoleA, Guid PeerRoleB,
        Guid AdminA, Guid AdminRevoked, Guid Stranger);

    /// <summary>
    /// Two brands. Brand A holds three roles at ranks 20/30/40; brand B holds one at 30. A system
    /// role (brand_id NULL, is_system) stands in for platform_admin. AdminA holds a live brand-A
    /// membership at rank 30, AdminRevoked holds the same one revoked, Stranger holds none.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var t = Guid.NewGuid().ToString("N")[..8];
        var brandA = Guid.NewGuid();
        var brandB = Guid.NewGuid();
        var platformId = Guid.NewGuid();

        // Tenancy rows as raw SQL, like the sibling fixtures — the real tables default columns the
        // EF entities require.
        await ExecAsync($"""
            INSERT INTO tenancy_org.platforms (id, code, name)
                VALUES ('{platformId}', 'P{t}', 'Guard platform {t}');
            INSERT INTO tenancy_org.brands (id, platform_id, name, code, status) VALUES
                ('{brandA}', '{platformId}', 'Guard brand A {t}', 'A{t}', 'active'),
                ('{brandB}', '{platformId}', 'Guard brand B {t}', 'B{t}', 'active');
            """);

        var systemRole  = RoleRow($"system.{t}",  brandId: null,   priority: 10, isSystem: true);
        var seniorRoleA = RoleRow($"senior.{t}",  brandId: brandA, priority: 20);
        var peerRoleA   = RoleRow($"peer.a.{t}",  brandId: brandA, priority: 30);
        var juniorRoleA = RoleRow($"junior.{t}",  brandId: brandA, priority: 40);
        var peerRoleB   = RoleRow($"peer.b.{t}",  brandId: brandB, priority: 30);

        var adminA       = Usr();
        var adminRevoked = Usr();
        var stranger     = Usr();

        await SeedEntitiesAsync(
        [
            systemRole, seniorRoleA, peerRoleA, juniorRoleA, peerRoleB,
            adminA, adminRevoked, stranger,
            Member(adminA.Id,       peerRoleA.Id, ScopeType.Brand, brandA, primary: true),
            Member(adminRevoked.Id, peerRoleA.Id, ScopeType.Brand, brandA,
                   revoked: DateTimeOffset.UtcNow.AddDays(-1)),
        ]);

        return new World(
            brandA, brandB,
            systemRole.Id, seniorRoleA.Id, peerRoleA.Id, juniorRoleA.Id, peerRoleB.Id,
            adminA.Id, adminRevoked.Id, stranger.Id);
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
    private static ICurrentUser PlatformActor() => new StubCurrentUser(null, platformAdmin: true);

    private static Role RoleRow(string code, Guid? brandId, short priority, bool isSystem = false) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = code, BrandId = brandId,
        ScopeType = ScopeType.Brand, IsSystem = isSystem, IsAssignable = true,
        Priority = priority, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static UserScopeMembership Member(
        Guid userId, Guid roleId, string scopeType, Guid? scopeId,
        bool primary = false, DateTimeOffset? revoked = null) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, RoleId = roleId, ScopeType = scopeType,
        ScopeId = scopeId, IsPrimary = primary, GrantedAt = DateTimeOffset.UtcNow,
        RevokedAt = revoked, Metadata = "{}", CreatedAt = DateTimeOffset.UtcNow,
    };

    private static User Usr() => new()
    {
        Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@t.test", UserType = UserType.Staff,
        Locale = "en-IN", Timezone = "Asia/Kolkata", Status = "active", Version = 1, PermVersion = 0,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Only <see cref="ICurrentUser.IsPlatformAdmin"/> and <see cref="ICurrentUser.BrandId"/>
    /// are read by the guard.</summary>
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
