using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>EF-faithful lock-in of AuditSaveChangesInterceptor (docs/rbac.md #12): the systematic audit
/// trail stamps brand_id/actor from the request context (NOT the entity), honours the denylist, and only
/// works because the current-month audit_logs partition is provisioned by the fixture bootstrap.</summary>
[Collection("rbac-ef")]
public sealed class AuditInterceptorTests
{
    private readonly RbacEfFixture _fx;
    public AuditInterceptorTests(RbacEfFixture fx) => _fx = fx;

    // 6 ── an audited write lands exactly one row, stamped with the request tenant + actor.
    [Fact]
    public async Task audit_row_lands_with_tenant_brand()
    {
        if (!_fx.DockerAvailable) return;

        var brand = Guid.NewGuid();  // arbitrary — proves the row's brand_id comes from ICurrentTenant
        var actor = Guid.NewGuid();
        var interceptor = Interceptor(brand, actor);

        var role = RoleRow($"audited.{Guid.NewGuid():N}");
        await using (var db = _fx.NewContext(new IInterceptor[] { interceptor }))
        {
            db.Add(role);
            await db.SaveChangesAsync();
        }

        await using var c = await _fx.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT action, resource_type, brand_id, actor_user_id, (new_values IS NOT NULL) " +
            "FROM identity_access.audit_logs WHERE resource_id = @rid", c);
        cmd.Parameters.AddWithValue("rid", role.Id);
        await using var r = await cmd.ExecuteReaderAsync();

        Assert.True(await r.ReadAsync(), "expected exactly one audit row for the created role");
        Assert.Equal("roles.created", r.GetString(0));
        Assert.Equal("roles", r.GetString(1));
        Assert.Equal(brand, r.GetGuid(2));   // from ICurrentTenant, not the Role entity
        Assert.Equal(actor, r.GetGuid(3));   // from ICurrentUser
        Assert.True(r.GetBoolean(4));        // new_values populated
        Assert.False(await r.ReadAsync());   // exactly one
    }

    // 6b ── secret/PII marker fields (password_hash, mfa_secret, email, phone) are masked to
    //        "[redacted]" in the audit snapshot — plaintext must NEVER land in the 7-year table.
    [Fact]
    public async Task audit_redacts_secret_and_pii_marker_fields()
    {
        if (!_fx.DockerAvailable) return;

        var interceptor = Interceptor(Guid.NewGuid(), Guid.NewGuid());

        const string secretHash = "PLAINTEXT_HASH_MUST_NOT_APPEAR";
        const string secretMfa  = "PLAINTEXT_MFA_MUST_NOT_APPEAR";
        const string phone      = "+919812345678";
        var email = $"audit.{Guid.NewGuid():N}@example.com";

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PhoneE164 = phone,
            PasswordHash = secretHash,   // "hash"/"password" marker
            MfaSecret = secretMfa,       // "secret" marker
            UserType = "staff", Locale = "en-IN", Timezone = "Asia/Kolkata", Status = "active",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        await using (var db = _fx.NewContext(new IInterceptor[] { interceptor }))
        {
            db.Add(user);
            await db.SaveChangesAsync();
        }

        await using var c = await _fx.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT new_values::text FROM identity_access.audit_logs WHERE resource_id = @rid", c);
        cmd.Parameters.AddWithValue("rid", user.Id);
        var newValues = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(newValues);
        Assert.Contains("[redacted]", newValues);        // marker fields are masked
        Assert.DoesNotContain(secretHash, newValues);    // password_hash never plaintext
        Assert.DoesNotContain(secretMfa, newValues);     // mfa_secret never plaintext
        Assert.DoesNotContain(email, newValues);         // email (PII marker) masked
        Assert.DoesNotContain(phone, newValues);         // phone (PII marker) masked
    }

    // 7 ── a denylisted entity (RefreshToken) writes no audit row.
    [Fact]
    public async Task audit_denylisted_entity_writes_no_row()
    {
        if (!_fx.DockerAvailable) return;
        var now = DateTimeOffset.UtcNow;

        var interceptor = Interceptor(Guid.NewGuid(), Guid.NewGuid());
        await using (var db = _fx.NewContext(new IInterceptor[] { interceptor }))
        {
            db.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),                 // satisfies CHECK(user_id OR customer_id)
                TokenHash = Guid.NewGuid().ToString("N"),
                FamilyId = Guid.NewGuid(),
                IssuedAt = now,
                ExpiresAt = now.AddDays(1),
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var rows = await _fx.ScalarLongAsync(
            "SELECT count(*) FROM identity_access.audit_logs WHERE resource_type = 'refresh_tokens'");
        Assert.Equal(0, rows);
    }

    // 8 ── the fixture bootstrap must leave a partition covering the current UTC month, or every audit
    //       INSERT would fail with "no partition of relation audit_logs found".
    [Fact]
    public async Task audit_partition_precondition_present()
    {
        if (!_fx.DockerAvailable) return;

        var partition = "audit_logs_p" + DateTime.UtcNow.ToString("yyyyMM") + "01";
        var n = await _fx.ScalarLongAsync("""
            SELECT count(*)
            FROM pg_inherits i
            JOIN pg_class c      ON c.oid = i.inhrelid
            JOIN pg_class p      ON p.oid = i.inhparent
            JOIN pg_namespace ns ON ns.oid = p.relnamespace
            WHERE ns.nspname = 'identity_access' AND p.relname = 'audit_logs' AND c.relname = @name
            """, ("name", partition));
        Assert.True(n >= 1, $"expected current-month audit partition {partition} to exist");
    }

    private static AuditSaveChangesInterceptor Interceptor(Guid brand, Guid actor) =>
        new(new FakeCurrentTenant { BrandId = brand, UserId = actor },
            new FakeCurrentUser { UserId = actor },
            new HttpContextAccessor { HttpContext = null },
            NullLogger<AuditSaveChangesInterceptor>.Instance);

    private static Role RoleRow(string code) => new()
    {
        Id = Guid.NewGuid(), Code = code, Name = code, ScopeType = ScopeType.Platform,
        IsSystem = false, IsAssignable = true, Priority = 100, Status = "active",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };
}
