using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Acceptance gate for the RaaS partner INVOICES (FULL-10 / issue #14): proves PostgreSQL RLS enforces
/// cross-partner isolation on commerce.partner_invoices for the NON-superuser, NON-bypassrls
/// <c>app_user</c> role via the <c>rls_partner</c> policy (kernel.current_partner_id() =
/// app.current_partner_id).
///
/// A partner session (SetRlsAsync(partner: P)) must see ONLY its own invoices and must be unable to
/// read or INSERT another partner's invoice (42501 on WITH CHECK). Ground truth is seeded on the
/// SUPERUSER connection (table owner → bypasses RLS) BEFORE any app_user assertion, because once RLS
/// is on the WITH CHECK would itself block the cross-partner seed. Every test uses fresh Guids so the
/// shared container stays interference-free.
/// </summary>
[Collection("rbac-rls")]
public sealed class PartnerInvoiceRlsTests
{
    private readonly RbacRlsFixture _fx;

    public PartnerInvoiceRlsTests(RbacRlsFixture fx) => _fx = fx;

    // ---- seed helpers (run on the superuser connection, which bypasses RLS) --

    private static async Task SeedPartnerAsync(NpgsqlConnection su, Guid partnerId, string code)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO logistics.partners (id, code, status) VALUES (@id, @c, 'active')", su);
        cmd.Parameters.AddWithValue("@id", partnerId);
        cmd.Parameters.AddWithValue("@c", code);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedInvoiceAsync(
        NpgsqlConnection su, Guid partnerId, string number, decimal grandTotal)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO commerce.partner_invoices (id, partner_id, invoice_number, grand_total, status) " +
            "VALUES (@id, @p, @n, @g, 'issued')", su);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@p", partnerId);
        cmd.Parameters.AddWithValue("@n", number);
        cmd.Parameters.AddWithValue("@g", grandTotal);
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

    /// <summary>THE acceptance gate: partner P1 sees only its own invoices; the superuser sees both.</summary>
    [Fact]
    public async Task Partner_sees_only_own_invoices()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();
        Guid invP1, invP2;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            invP1 = await SeedInvoiceAsync(su, p1, $"INV-P1-{p1:N}", 1500m);
            invP2 = await SeedInvoiceAsync(su, p2, $"INV-P2-{p2:N}", 2600m);

            // superuser (table owner) sees both invoices.
            Assert.Equal(2L, await CountAsync(su,
                "SELECT count(*) FROM commerce.partner_invoices WHERE partner_id = ANY(@ps)",
                ("@ps", new[] { p1, p2 })));
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, partner: p1);

        // P1 sees exactly one invoice — its own.
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_invoices WHERE partner_id = ANY(@ps)",
            ("@ps", new[] { p1, p2 })));
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_invoices WHERE id = @id", ("@id", invP1)));
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_invoices WHERE id = @id", ("@id", invP2)));
    }

    /// <summary>
    /// A partner cannot READ (0 rows, USING) or INSERT (42501, WITH CHECK) another partner's invoice;
    /// same-partner writes succeed and the generated amount_due is correct.
    /// </summary>
    [Fact]
    public async Task Partner_cannot_read_or_write_another_partners_invoice()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            await SeedInvoiceAsync(su, p2, $"INV-P2-{p2:N}", 900m); // P2's invoice — invisible to P1.
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, partner: p1);

        // Read of P2's invoice is silently filtered to nothing (USING).
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_invoices WHERE partner_id = @p", ("@p", p2)));

        // Cross-partner INSERT is blocked by WITH CHECK -> 42501.
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO commerce.partner_invoices (partner_id, invoice_number, grand_total) " +
                "VALUES (@p, @n, 100)", app);
            cmd.Parameters.AddWithValue("@p", p2);
            cmd.Parameters.AddWithValue("@n", $"INV-BAD-{Guid.NewGuid():N}");
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", ex.SqlState);

        // Same-partner INSERT succeeds; amount_due = grand_total - amount_paid is generated.
        await using (var ok = new NpgsqlCommand(
            "INSERT INTO commerce.partner_invoices (partner_id, invoice_number, grand_total, amount_paid) " +
            "VALUES (@p, @n, 500, 200) RETURNING amount_due", app))
        {
            ok.Parameters.AddWithValue("@p", p1);
            ok.Parameters.AddWithValue("@n", $"INV-P1-{Guid.NewGuid():N}");
            var due = (decimal)(await ok.ExecuteScalarAsync())!;
            Assert.Equal(300m, due);
        }
    }

    /// <summary>The platform-admin / worker / webhook path (bypass = true) sees every partner's invoice.</summary>
    [Fact]
    public async Task Partner_invoice_bypass_sees_all()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            await SeedInvoiceAsync(su, p1, $"INV-P1-{p1:N}", 100m);
            await SeedInvoiceAsync(su, p2, $"INV-P2-{p2:N}", 200m);
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, bypass: true);

        Assert.Equal(2L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_invoices WHERE partner_id = ANY(@ps)",
            ("@ps", new[] { p1, p2 })));
    }
}
