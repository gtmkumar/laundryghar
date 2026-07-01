using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// DUAL-VISIBILITY acceptance gate for the RaaS partner dispatch table (FULL-11b / issue #14):
/// proves the COMBINED <c>rls_partner_or_brand</c> policy on logistics.partner_dispatches gives a
/// dispatch visibility to BOTH the owning partner AND the serving brand's fleet, while still
/// isolating across partners and across brands, for the NON-superuser, NON-bypassrls
/// <c>app_user</c> role.
///
/// Ground truth is seeded on the SUPERUSER connection (table owner → bypasses RLS) BEFORE any
/// app_user assertion. Every test uses fresh Guids so the collection-shared container stays
/// interference-free.
/// </summary>
[Collection("rbac-rls")]
public sealed class PartnerDispatchRlsTests
{
    private readonly RbacRlsFixture _fx;

    public PartnerDispatchRlsTests(RbacRlsFixture fx) => _fx = fx;

    // ---- seed helpers (run on the superuser connection, which bypasses RLS) --

    private static async Task SeedPartnerAsync(NpgsqlConnection su, Guid partnerId, string code)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO logistics.partners (id, code, status) VALUES (@id, @c, 'active')", su);
        cmd.Parameters.AddWithValue("@id", partnerId);
        cmd.Parameters.AddWithValue("@c", code);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedBookingAsync(NpgsqlConnection su, Guid partnerId, Guid? brandId)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO logistics.partner_bookings (id, partner_id, brand_id, status) " +
            "VALUES (@id, @p, @b, 'requested')", su);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@p", partnerId);
        cmd.Parameters.AddWithValue("@b", (object?)brandId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<Guid> SeedDispatchAsync(
        NpgsqlConnection su, Guid partnerId, Guid bookingId, Guid? brandId)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO logistics.partner_dispatches (id, partner_id, partner_booking_id, brand_id, status) " +
            "VALUES (@id, @p, @bk, @b, 'assigned')", su);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@p", partnerId);
        cmd.Parameters.AddWithValue("@bk", bookingId);
        cmd.Parameters.AddWithValue("@b", (object?)brandId ?? DBNull.Value);
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

    /// <summary>(a) A partner session sees ONLY its own dispatches (partner arm), never another
    /// partner's — even across the same booking id space.</summary>
    [Fact]
    public async Task Partner_sees_only_own_dispatches()
    {
        if (!_fx.DockerAvailable) { return; }

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();
        Guid brand = Guid.NewGuid();
        Guid dispatchP1, dispatchP2;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            var bkP1 = await SeedBookingAsync(su, p1, brand);
            var bkP2 = await SeedBookingAsync(su, p2, brand);
            dispatchP1 = await SeedDispatchAsync(su, p1, bkP1, brand);
            dispatchP2 = await SeedDispatchAsync(su, p2, bkP2, brand);
        }

        await using var app = await _fx.OpenAppUserAsync();
        // Partner session: partner_id set, NO brand context.
        await RbacRlsFixture.SetRlsAsync(app, partner: p1);

        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_dispatches WHERE id = @id", ("@id", dispatchP1)));
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_dispatches WHERE id = @id", ("@id", dispatchP2)));
    }

    /// <summary>(b) THE dual-visibility gate: a BRAND-staff session (brand_id set, NO partner) sees
    /// the dispatches its OWN fleet serves — but NOT dispatches served by another brand, and NOT a
    /// dispatch whose brand_id is NULL (the brand arm is guarded by brand_id IS NOT NULL).</summary>
    [Fact]
    public async Task Brand_staff_sees_own_brand_dispatches_only()
    {
        if (!_fx.DockerAvailable) { return; }

        Guid partnerA = Guid.NewGuid(), partnerB = Guid.NewGuid();
        Guid brand1 = Guid.NewGuid(), brand2 = Guid.NewGuid();
        Guid dispBrand1, dispBrand2, dispNoBrand;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, partnerA, $"A-{partnerA:N}");
            await SeedPartnerAsync(su, partnerB, $"B-{partnerB:N}");

            // partnerA's booking served by brand1; partnerB's served by brand2; partnerA's second
            // booking has NO serving brand yet.
            var bkA1 = await SeedBookingAsync(su, partnerA, brand1);
            var bkB2 = await SeedBookingAsync(su, partnerB, brand2);
            var bkA0 = await SeedBookingAsync(su, partnerA, null);

            dispBrand1  = await SeedDispatchAsync(su, partnerA, bkA1, brand1);
            dispBrand2  = await SeedDispatchAsync(su, partnerB, bkB2, brand2);
            dispNoBrand = await SeedDispatchAsync(su, partnerA, bkA0, null);
        }

        await using var app = await _fx.OpenAppUserAsync();
        // Brand-staff session: brand_id set (fleet brand1), NO partner context.
        await RbacRlsFixture.SetRlsAsync(app, brand: brand1);

        // Sees its own brand's dispatch...
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_dispatches WHERE id = @id", ("@id", dispBrand1)));
        // ...but NOT another brand's...
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_dispatches WHERE id = @id", ("@id", dispBrand2)));
        // ...and NOT a NULL-brand dispatch (brand arm requires brand_id IS NOT NULL).
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_dispatches WHERE id = @id", ("@id", dispNoBrand)));
    }

    /// <summary>(b') The partner still tracks the SAME row the brand staff manage — proving both
    /// arms resolve the one dispatch (true dual visibility, not a copy).</summary>
    [Fact]
    public async Task Same_dispatch_visible_to_both_partner_and_brand()
    {
        if (!_fx.DockerAvailable) { return; }

        Guid partner = Guid.NewGuid(), brand = Guid.NewGuid();
        Guid dispatch;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, partner, $"P-{partner:N}");
            var bk = await SeedBookingAsync(su, partner, brand);
            dispatch = await SeedDispatchAsync(su, partner, bk, brand);
        }

        // Partner arm sees it.
        await using (var partnerSession = await _fx.OpenAppUserAsync())
        {
            await RbacRlsFixture.SetRlsAsync(partnerSession, partner: partner);
            Assert.Equal(1L, await CountAsync(partnerSession,
                "SELECT count(*) FROM logistics.partner_dispatches WHERE id = @id", ("@id", dispatch)));
        }

        // Brand arm sees the very same row.
        await using (var brandSession = await _fx.OpenAppUserAsync())
        {
            await RbacRlsFixture.SetRlsAsync(brandSession, brand: brand);
            Assert.Equal(1L, await CountAsync(brandSession,
                "SELECT count(*) FROM logistics.partner_dispatches WHERE id = @id", ("@id", dispatch)));
        }
    }

    /// <summary>(c) Cross-partner AND cross-brand INSERTs are both blocked by WITH CHECK (42501),
    /// while an INSERT that satisfies either arm succeeds.</summary>
    [Fact]
    public async Task Cross_partner_and_cross_brand_insert_blocked()
    {
        if (!_fx.DockerAvailable) { return; }

        Guid partner = Guid.NewGuid(), otherPartner = Guid.NewGuid();
        Guid brand = Guid.NewGuid(), otherBrand = Guid.NewGuid();
        Guid bookingOwn, bookingOther;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, partner, $"P-{partner:N}");
            await SeedPartnerAsync(su, otherPartner, $"O-{otherPartner:N}");
            bookingOwn   = await SeedBookingAsync(su, partner, brand);
            bookingOther = await SeedBookingAsync(su, otherPartner, otherBrand);
        }

        await using var app = await _fx.OpenAppUserAsync();
        // A brand-staff session for `brand`, no partner context.
        await RbacRlsFixture.SetRlsAsync(app, brand: brand);

        // Cross-brand insert (brand_id = otherBrand, partner arm inert) → 42501.
        var crossBrand = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO logistics.partner_dispatches (partner_id, partner_booking_id, brand_id, status) " +
                "VALUES (@p, @bk, @b, 'assigned')", app);
            cmd.Parameters.AddWithValue("@p", otherPartner);
            cmd.Parameters.AddWithValue("@bk", bookingOther);
            cmd.Parameters.AddWithValue("@b", otherBrand);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", crossBrand.SqlState);

        // Same-brand insert (brand arm passes) → succeeds even though partner_id is another org's:
        // the brand fleet legitimately serves that partner's booking.
        await using (var ok = new NpgsqlCommand(
            "INSERT INTO logistics.partner_dispatches (partner_id, partner_booking_id, brand_id, status) " +
            "VALUES (@p, @bk, @b, 'assigned')", app))
        {
            ok.Parameters.AddWithValue("@p", partner);
            ok.Parameters.AddWithValue("@bk", bookingOwn);
            ok.Parameters.AddWithValue("@b", brand);
            Assert.Equal(1, await ok.ExecuteNonQueryAsync());
        }

        // A partner session likewise cannot insert a dispatch for another partner even if it forges
        // a NULL brand (both arms fail) → 42501.
        await RbacRlsFixture.SetRlsAsync(app, partner: partner);
        var crossPartner = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO logistics.partner_dispatches (partner_id, partner_booking_id, status) " +
                "VALUES (@p, @bk, 'pending')", app);
            cmd.Parameters.AddWithValue("@p", otherPartner);
            cmd.Parameters.AddWithValue("@bk", bookingOther);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", crossPartner.SqlState);
    }

    /// <summary>(d) The platform-admin / worker path (bypass = true) sees every dispatch.</summary>
    [Fact]
    public async Task Bypass_sees_all_dispatches()
    {
        if (!_fx.DockerAvailable) { return; }

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();
        Guid brand1 = Guid.NewGuid(), brand2 = Guid.NewGuid();
        Guid d1, d2;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            var bk1 = await SeedBookingAsync(su, p1, brand1);
            var bk2 = await SeedBookingAsync(su, p2, brand2);
            d1 = await SeedDispatchAsync(su, p1, bk1, brand1);
            d2 = await SeedDispatchAsync(su, p2, bk2, brand2);
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, bypass: true);

        Assert.Equal(2L, await CountAsync(app,
            "SELECT count(*) FROM logistics.partner_dispatches WHERE id = ANY(@ids)",
            ("@ids", new[] { d1, d2 })));
    }
}
