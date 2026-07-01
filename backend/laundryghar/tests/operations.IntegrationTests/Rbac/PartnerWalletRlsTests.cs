using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// Acceptance gate for the RaaS partner PREPAID WALLET (FULL-9 / issue #14): proves PostgreSQL RLS
/// enforces cross-partner isolation on commerce.partner_wallet_accounts + partner_wallet_transactions
/// for the NON-superuser, NON-bypassrls <c>app_user</c> role via the <c>rls_partner</c> policy
/// (kernel.current_partner_id() = app.current_partner_id).
///
/// A partner session (SetRlsAsync(partner: P)) must see ONLY its own wallet + ledger and must be
/// unable to read or INSERT another partner's wallet/ledger. Ground truth is seeded on the SUPERUSER
/// connection (table owner → bypasses RLS) BEFORE any app_user assertion, because once RLS is on the
/// WITH CHECK would itself block the cross-partner seed. RLS is role-agnostic — a partner_admin and a
/// partner_operator share the same app_user session and the same partner_id, so "balance read works
/// for both roles" is proven by a partner-scoped read (the role split is an API-layer gate, tested
/// separately). Every test uses fresh Guids so the shared container stays interference-free.
/// </summary>
[Collection("rbac-rls")]
public sealed class PartnerWalletRlsTests
{
    private readonly RbacRlsFixture _fx;

    public PartnerWalletRlsTests(RbacRlsFixture fx) => _fx = fx;

    // ---- seed helpers (run on the superuser connection, which bypasses RLS) --

    private static async Task SeedPartnerAsync(NpgsqlConnection su, Guid partnerId, string code)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO logistics.partners (id, code, status) VALUES (@id, @c, 'active')", su);
        cmd.Parameters.AddWithValue("@id", partnerId);
        cmd.Parameters.AddWithValue("@c", code);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> SeedWalletAsync(NpgsqlConnection su, Guid partnerId, decimal balance)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO commerce.partner_wallet_accounts (id, partner_id, currency_code, balance) " +
            "VALUES (@id, @p, 'INR', @b)", su);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@p", partnerId);
        cmd.Parameters.AddWithValue("@b", balance);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<Guid> SeedTxnAsync(
        NpgsqlConnection su, Guid walletId, Guid partnerId, short direction, decimal amount, string key)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO commerce.partner_wallet_transactions " +
            "(id, partner_wallet_account_id, partner_id, direction, amount, balance_before, balance_after, " +
            " reference_type, idempotency_key) " +
            "VALUES (@id, @w, @p, @d, @a, 0, @a, 'topup', @k)", su);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@w", walletId);
        cmd.Parameters.AddWithValue("@p", partnerId);
        cmd.Parameters.AddWithValue("@d", direction);
        cmd.Parameters.AddWithValue("@a", amount);
        cmd.Parameters.AddWithValue("@k", key);
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

    /// <summary>THE acceptance gate: partner P1 sees only its own wallet + ledger; the superuser both.</summary>
    [Fact]
    public async Task Partner_sees_only_own_wallet_and_ledger()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();
        Guid walletP1, walletP2, txnP1, txnP2;

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            walletP1 = await SeedWalletAsync(su, p1, 500m);
            walletP2 = await SeedWalletAsync(su, p2, 900m);
            txnP1 = await SeedTxnAsync(su, walletP1, p1, 1, 500m, $"k1-{p1:N}");
            txnP2 = await SeedTxnAsync(su, walletP2, p2, 1, 900m, $"k2-{p2:N}");

            // superuser (table owner) sees both wallets.
            Assert.Equal(2L, await CountAsync(su,
                "SELECT count(*) FROM commerce.partner_wallet_accounts WHERE partner_id = ANY(@ps)",
                ("@ps", new[] { p1, p2 })));
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, partner: p1);

        // P1 sees exactly one wallet — its own.
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_wallet_accounts WHERE partner_id = ANY(@ps)",
            ("@ps", new[] { p1, p2 })));
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_wallet_accounts WHERE id = @id", ("@id", walletP1)));
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_wallet_accounts WHERE id = @id", ("@id", walletP2)));

        // ...and exactly one ledger row — its own.
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_wallet_transactions WHERE partner_id = ANY(@ps)",
            ("@ps", new[] { p1, p2 })));
        Assert.Equal(1L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_wallet_transactions WHERE id = @id", ("@id", txnP1)));
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_wallet_transactions WHERE id = @id", ("@id", txnP2)));
    }

    /// <summary>
    /// A partner cannot READ (0 rows, USING) or INSERT (42501, WITH CHECK) another partner's wallet
    /// or ledger; same-partner writes succeed.
    /// </summary>
    [Fact]
    public async Task Partner_cannot_read_or_write_another_partners_wallet()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            await SeedWalletAsync(su, p2, 900m); // P2's wallet — invisible to P1.
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, partner: p1);

        // Read of P2's wallet is silently filtered to nothing (USING).
        Assert.Equal(0L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_wallet_accounts WHERE partner_id = @p", ("@p", p2)));

        // Cross-partner wallet INSERT is blocked by WITH CHECK -> 42501.
        var walletEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO commerce.partner_wallet_accounts (partner_id, currency_code, balance) " +
                "VALUES (@p, 'INR', 0)", app);
            cmd.Parameters.AddWithValue("@p", p2);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", walletEx.SqlState);

        // Cross-partner ledger INSERT is blocked too.
        var txnEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO commerce.partner_wallet_transactions (partner_id, direction, amount) " +
                "VALUES (@p, 1, 10)", app);
            cmd.Parameters.AddWithValue("@p", p2);
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("42501", txnEx.SqlState);

        // Same-partner wallet INSERT succeeds (creating P1's own wallet).
        await using (var ok = new NpgsqlCommand(
            "INSERT INTO commerce.partner_wallet_accounts (partner_id, currency_code, balance) " +
            "VALUES (@p, 'INR', 0)", app))
        {
            ok.Parameters.AddWithValue("@p", p1);
            Assert.Equal(1, await ok.ExecuteNonQueryAsync());
        }
    }

    /// <summary>
    /// Balance read works for a partner session (representing BOTH partner roles — RLS is role-blind;
    /// the operator/admin split is enforced at the API layer, not in the policy). The generated
    /// available_balance column is readable and equals balance - locked_balance.
    /// </summary>
    [Fact]
    public async Task Partner_balance_read_works()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedWalletAsync(su, p1, 750m);
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, partner: p1);

        await using var cmd = new NpgsqlCommand(
            "SELECT available_balance FROM commerce.partner_wallet_accounts WHERE partner_id = @p", app);
        cmd.Parameters.AddWithValue("@p", p1);
        var available = (decimal)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(750m, available);
    }

    /// <summary>The platform-admin / worker path (bypass = true) sees every partner's wallet.</summary>
    [Fact]
    public async Task Partner_wallet_bypass_sees_all()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();

        await using (var su = await _fx.OpenSuperuserAsync())
        {
            await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
            await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
            await SeedWalletAsync(su, p1, 100m);
            await SeedWalletAsync(su, p2, 200m);
        }

        await using var app = await _fx.OpenAppUserAsync();
        await RbacRlsFixture.SetRlsAsync(app, bypass: true);

        Assert.Equal(2L, await CountAsync(app,
            "SELECT count(*) FROM commerce.partner_wallet_accounts WHERE partner_id = ANY(@ps)",
            ("@ps", new[] { p1, p2 })));
    }

    /// <summary>
    /// Idempotency is scoped PER PARTNER, not globally: two DIFFERENT partners may use the same
    /// caller-supplied key (the composite UNIQUE(partner_id, idempotency_key) permits it), while the
    /// SAME partner reusing a key is rejected (23505). Guards the money-correctness regression a global
    /// UNIQUE(idempotency_key) caused — partner B being permanently unable to top up with a key partner A
    /// already used. Seeded/asserted on the superuser connection (table owner) so the test isolates the
    /// CONSTRAINT shape, independent of RLS.
    /// </summary>
    [Fact]
    public async Task Idempotency_key_is_unique_per_partner_not_globally()
    {
        if (!_fx.DockerAvailable) { return; } // Docker not available: skip (xunit 2.9.2 lacks Assert.Skip)

        Guid p1 = Guid.NewGuid(), p2 = Guid.NewGuid();
        var sharedKey = $"topup-{Guid.NewGuid():N}";

        await using var su = await _fx.OpenSuperuserAsync();
        await SeedPartnerAsync(su, p1, $"P1-{p1:N}");
        await SeedPartnerAsync(su, p2, $"P2-{p2:N}");
        var walletP1 = await SeedWalletAsync(su, p1, 0m);
        var walletP2 = await SeedWalletAsync(su, p2, 0m);

        // Partner 1 uses the key — succeeds.
        await SeedTxnAsync(su, walletP1, p1, 1, 100m, sharedKey);

        // Partner 2 reuses the SAME key — must ALSO succeed (uniqueness is per-partner).
        await SeedTxnAsync(su, walletP2, p2, 1, 250m, sharedKey);

        // Partner 1 reusing its OWN key — rejected by the composite unique (23505).
        var dup = await Assert.ThrowsAsync<PostgresException>(
            () => SeedTxnAsync(su, walletP1, p1, 1, 100m, sharedKey));
        Assert.Equal("23505", dup.SqlState);

        // Both partners' first credits persisted.
        Assert.Equal(1L, await CountAsync(su,
            "SELECT count(*) FROM commerce.partner_wallet_transactions WHERE partner_id = @p AND idempotency_key = @k",
            ("@p", p1), ("@k", sharedKey)));
        Assert.Equal(1L, await CountAsync(su,
            "SELECT count(*) FROM commerce.partner_wallet_transactions WHERE partner_id = @p AND idempotency_key = @k",
            ("@p", p2), ("@k", sharedKey)));
    }
}
