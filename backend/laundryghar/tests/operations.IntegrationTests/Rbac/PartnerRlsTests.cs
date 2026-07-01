using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Acceptance gate for the RaaS partner MVP (issue #14): proves PostgreSQL RLS enforces
/// cross-partner isolation for the NON-superuser, NON-bypassrls <c>app_user</c> role via
/// the <c>rls_partner</c> policy (kernel.current_partner_id() = app.current_partner_id).
///
/// A partner session (SetRlsAsync(partner: P)) must see ONLY its own logistics.partner_bookings
/// rows and must be unable to INSERT a booking for another partner. Ground truth is seeded on
/// the SUPERUSER connection (table owner → bypasses RLS) BEFORE any app_user assertion, because
/// once RLS is on the app_user WITH CHECK would itself block the cross-partner seed rows.
/// Every test uses fresh Guids so the collection-shared container stays interference-free.
/// </summary>
[Collection("rbac-rls")]
public sealed class PartnerRlsTests
{
    private readonly RbacRlsFixture _fx;

    public PartnerRlsTests(RbacRlsFixture fx) => _fx = fx;

    // ---- seed helpers (run on the superuser connection, which bypasses RLS) --

    private static async Task SeedPartnerAsync(NpgsqlConnection su, Guid partnerId, string code)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO logistics.partners (id, code, status) VALUES (@id, @c, 'active')", su);
        cmd.Parameters.AddWithValue("@id", partnerId);
        cmd.Parameters.AddWithValue("@c", code);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedBookingAsync(NpgsqlConnection su, Guid partnerId)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO logistics.partner_bookings (id, partner_id, status) " +
            "VALUES (@id, @p, 'requested')", su);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@p", partnerId);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, string sql, params (string, object)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ---- tests --------------------------------------------------------------

    /// <summary>
    /// THE acceptance gate: partner P1 sees only its own booking; the superuser sees both.
    /// </summary>
    [Fact]
    public async Task Partner_sees_only_own_bookings()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();
        Guid bookingP1, bookingP2;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            bookingP1 = await SeedBookingAsync(su, p1);
            bookingP2 = await SeedBookingAsync(su, p2);

            // superuser (table owner) sees both bookings.
            Assert.Equal(2L, await CountAsync(su,
                "SELECT count(*) FROM logistics.partner_bookings WHERE partner_id = ANY(@ps)",
                ("@ps", new[] { p1, p2 })));
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, partner: p1);

        // P1's session sees exactly one booking...
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_bookings WHERE partner_id = ANY(@ps)",
            ("@ps", new[] { p1, p2 })));
        // ...and it is specifically P1's booking, never P2's.
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_bookings WHERE id = @id", ("@id", bookingP1)));
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_bookings WHERE id = @id", ("@id", bookingP2)));
    }

    /// <summary>
    /// A partner cannot INSERT a booking on behalf of another partner (WITH CHECK → 42501).
    /// </summary>
    [Fact]
    public async Task Partner_cannot_insert_booking_for_another_partner()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, partner: p1);

        // cross-partner insert is blocked by the WITH CHECK clause -> 42501.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO logistics.partner_bookings (partner_id, status) VALUES (@p, 'requested')", app);
            cmd.Parameters.AddWithValue("@p", p2);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", ex.SqlState);

        // same-partner insert succeeds.
        await using (var ok = new NpgsqlCommand(
            "INSERT INTO logistics.partner_bookings (partner_id, status) VALUES (@p, 'requested')", app))
        {
            ok.Parameters.AddWithValue("@p", p1);
            Assert.Equal(1, await ok.ExecuteNonQueryAsync());
        }
    }

    /// <summary>
    /// The platform-admin / worker path (bypass = true) sees every partner's bookings.
    /// </summary>
    [Fact]
    public async Task Partner_bypass_sees_all()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            await SeedBookingAsync(su, p1);
            await SeedBookingAsync(su, p2);
        }

        await using var app = await _fx.OpenAppUserAsync();
        // platform-admin/worker path: hardened kernel.rls_bypass() lifts partner isolation.
        await RbacRlsFixture.SetRlsAsync(app, bypass: true);

        Assert.Equal(2L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_bookings WHERE partner_id = ANY(@ps)",
            ("@ps", new[] { p1, p2 })));
    }

    /// <summary>
    /// No partner context (current_partner_id NULL, no bypass) → nothing is visible.
    /// NULL matches no partner_id, so the session is fully opaque — no accidental leakage.
    /// </summary>
    [Fact]
    public async Task Partner_with_no_partner_context_sees_nothing()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            await SeedBookingAsync(su, p1);
            await SeedBookingAsync(su, p2);
        }

        await using var app = await _fx.OpenAppUserAsync();
        // partner: null → app.current_partner_id = '' → kernel.current_partner_id() = NULL.
        await RbacRlsFixture.SetRlsAsync(app, partner: null, bypass: false);

        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_bookings WHERE partner_id = ANY(@ps)",
            ("@ps", new[] { p1, p2 })));
    }
}
