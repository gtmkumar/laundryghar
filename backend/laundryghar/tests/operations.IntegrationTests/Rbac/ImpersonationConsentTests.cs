using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// §8.1: the platform must not touch a provider's data "without recorded consent". §7 adds
/// "read-first" and "full audit".
///
/// <para>Four properties are enforced in SQL rather than trusted to application code, because an
/// application-only rule is one refactor away from not existing. These tests drive the constraints
/// directly — they try to create the bad states and expect the database to refuse.</para>
/// </summary>
[Collection("rbac-rls")]
public sealed class ImpersonationConsentTests
{
    private readonly RbacRlsFixture _fx;
    public ImpersonationConsentTests(RbacRlsFixture fx) => _fx = fx;

    // 1 ── TIME-BOXED. An approved grant with no expiry is a session that never ends — the exact
    //      failure this table exists to prevent.
    [Fact]
    public async Task An_approved_grant_cannot_exist_without_an_expiry()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants
                (brand_id, support_user_id, reason, status, approved_by_user_id, approved_at)
            VALUES ('{brand}', '{user}', 'no expiry', 'approved', '{user}', now())
            """));

        Assert.Equal("23514", ex.SqlState);   // check_violation
    }

    // 2 ── the 24h ceiling. A grant that can be approved for a year is not time-boxed, it is
    //      permanent access with extra paperwork.
    [Fact]
    public async Task An_approval_cannot_run_longer_than_a_day()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants
                (brand_id, support_user_id, reason, status, approved_by_user_id, approved_at, expires_at)
            VALUES ('{brand}', '{user}', 'forever', 'approved', '{user}', now(), now() + interval '25 hours')
            """));

        Assert.Equal("23514", ex.SqlState);

        // …and 23 hours is fine, so the constraint is a ceiling and not a blanket refusal.
        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants
                (brand_id, support_user_id, reason, status, approved_by_user_id, approved_at, expires_at)
            VALUES ('{brand}', '{user}', 'a long support call', 'approved', '{user}', now(), now() + interval '23 hours')
            """);
    }

    // 3 ── one live grant per (brand, person): overlapping grants would let a revoked engineer fall
    //      back to an older approval.
    [Fact]
    public async Task A_person_cannot_hold_two_live_grants_on_one_brand()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn);

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants (brand_id, support_user_id, reason)
            VALUES ('{brand}', '{user}', 'first')
            """);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants (brand_id, support_user_id, reason)
            VALUES ('{brand}', '{user}', 'second')
            """));

        Assert.Equal("23505", ex.SqlState);   // unique_violation

        // Once the first is finished, asking again is allowed — the index covers LIVE rows only.
        await RbacRlsFixture.ExecAsync(conn,
            $"UPDATE identity_access.impersonation_grants SET status='revoked', revoked_at=now() WHERE brand_id='{brand}'");
        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants (brand_id, support_user_id, reason)
            VALUES ('{brand}', '{user}', 'after the last one ended')
            """);
    }

    // 4 ── the state oracle reports 'expired' the moment the window passes, with no sweep job. Time
    //      passing must end a session by itself.
    [Fact]
    public async Task A_window_that_has_passed_reports_expired_without_a_sweep()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn);

        // Written past-dated through the constraint's own rules: approved an hour ago, for 1 minute.
        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO identity_access.impersonation_grants
                (id, brand_id, support_user_id, reason, status, approved_by_user_id, approved_at, expires_at)
            VALUES ('11111111-1111-1111-1111-111111111111', '{brand}', '{user}', 'over',
                    'approved', '{user}', now() - interval '1 hour', now() - interval '59 minutes')
            """);

        var status = await RbacRlsFixture.ScalarAsync(conn,
            "SELECT status FROM kernel.impersonation_grant_state('11111111-1111-1111-1111-111111111111')");

        // The stored row still says 'approved'. The oracle — the thing the guard actually asks —
        // says otherwise, which is what makes expiry self-enforcing.
        Assert.Equal("expired", status);
        Assert.Equal("approved", await RbacRlsFixture.ScalarAsync(conn,
            "SELECT status FROM identity_access.impersonation_grants WHERE id = '11111111-1111-1111-1111-111111111111'"));
    }

    // 5 ── CONSENT. The request function is the only door support has, and it can only ever create a
    //      pending row. If it could create an approved one, the whole feature would be theatre.
    [Fact]
    public async Task Asking_can_only_ever_create_a_pending_request()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn);

        var id = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.request_impersonation('{brand}', '{user}', 'customer reported a stuck order', 'read_only')");

        var row = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status || '/' || scope FROM identity_access.impersonation_grants WHERE id = '{id}'");

        Assert.Equal("pending/read_only", row);
    }

    // 6 ── asking twice returns the SAME request rather than raising or, far worse, replacing a live
    //      approval with a fresh pending one (which would revoke access by accident).
    [Fact]
    public async Task Asking_twice_returns_the_same_request()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn);

        var first = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.request_impersonation('{brand}', '{user}', 'first ask', 'read_only')");
        await RbacRlsFixture.ExecAsync(conn, $"""
            UPDATE identity_access.impersonation_grants
               SET status='approved', approved_by_user_id='{user}', approved_at=now(),
                   expires_at=now() + interval '1 hour'
             WHERE id = '{first}'
            """);

        var second = await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT kernel.request_impersonation('{brand}', '{user}', 'asked again', 'read_write')");

        Assert.Equal(first?.ToString(), second?.ToString());
        // and the live approval is intact — not downgraded back to pending by the second ask.
        Assert.Equal("approved", await RbacRlsFixture.ScalarAsync(conn,
            $"SELECT status FROM identity_access.impersonation_grants WHERE id = '{first}'"));
    }

    // 7 ── a request with no reason is refused. The reason is what the owner reads when deciding;
    //      without it "consent" is a yes/no on nothing.
    [Fact]
    public async Task A_request_without_a_reason_is_refused()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();
        var (brand, user) = await SeedAsync(conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => RbacRlsFixture.ExecAsync(conn,
            $"SELECT kernel.request_impersonation('{brand}', '{user}', '   ', 'read_only')"));

        Assert.Contains("reason", ex.MessageText);
    }

    // 8 ── THE ASYMMETRY. Consent the grantee can grant itself is not consent, so no role may hold
    //      both halves. Migration 0014 asserts this at apply time; asserting it again here means a
    //      later grant edit that breaks it fails a test rather than passing silently.
    [Fact]
    public async Task No_role_can_both_request_and_approve()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();

        var both = await RbacRlsFixture.ScalarAsync(conn, """
            SELECT string_agg(DISTINCT r.code, ', ')
            FROM   identity_access.roles r
            JOIN   identity_access.role_permissions rq ON rq.role_id = r.id
            JOIN   identity_access.permissions pq ON pq.id = rq.permission_id AND pq.code = 'impersonation.request'
            JOIN   identity_access.role_permissions ra ON ra.role_id = r.id
            JOIN   identity_access.permissions pa ON pa.id = ra.permission_id AND pa.code = 'impersonation.approve'
            """);

        Assert.True(both is null or DBNull, $"role(s) can request AND approve their own access: {both}");
    }

    // 9 ── the audit trail carries the consent. Without this column an impersonated action is
    //      indistinguishable from ordinary work by the same person.
    [Fact]
    public async Task Audit_rows_can_carry_the_consent_they_happened_under()
    {
        if (!_fx.DockerAvailable) return;
        await using var conn = await _fx.OpenSuperuserAsync();

        var exists = await RbacRlsFixture.ScalarAsync(conn, """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema='identity_access' AND table_name='audit_logs'
              AND column_name='impersonation_grant_id'
            """);

        Assert.Equal(1L, Convert.ToInt64(exists));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A brand and a support user, both unique per test so the tests do not collide on the
    /// one-live-grant index.</summary>
    private static async Task<(Guid Brand, Guid User)> SeedAsync(NpgsqlConnection conn)
    {
        var brand = Guid.NewGuid();
        var user = Guid.NewGuid();
        var suffix = user.ToString("N")[..8];

        await RbacRlsFixture.ExecAsync(conn, $"""
            INSERT INTO tenancy_org.brands (id, name)
            VALUES ('{brand}', 'Impersonation test brand {suffix}');
            INSERT INTO identity_access.users (id, email, user_type)
            VALUES ('{user}', 'support-{suffix}@laundryghar.test', 'staff');
            """);

        return (brand, user);
    }
}
