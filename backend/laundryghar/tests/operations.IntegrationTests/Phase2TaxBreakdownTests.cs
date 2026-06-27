using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty;
using laundryghar.SharedDataModel.Entities.FinanceRoyalty.Subscriptions;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace operations.IntegrationTests;

/// <summary>
/// Phase-2 gate (slice 2F, the three-way tax coordination): all four invoice tables share one
/// tax_breakdown jsonb via the same EF owned type, and ONE atomic migration moves Orders +
/// Commerce + Finance together. SQL test needs Docker.
/// </summary>
public sealed class Phase2TaxBreakdownTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private string _connString = "";
    private bool _dockerAvailable = true;

    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        try { await _pg.StartAsync(); _connString = _pg.GetConnectionString(); }
        catch (Exception) { _dockerAvailable = false; }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    [Theory]
    [InlineData(typeof(Invoice))]
    [InlineData(typeof(SubscriptionInvoice))]
    [InlineData(typeof(RoyaltyInvoice))]
    [InlineData(typeof(FranchiseSubscriptionInvoice))]
    public void Every_invoice_maps_tax_to_the_same_tax_breakdown_jsonb(Type invoiceType)
    {
        var options = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=model_only", o => o.UseNetTopologySuite())
            .Options;
        using var ctx = new LaundryGharDbContext(options);
        var et = ctx.Model.FindEntityType(invoiceType)!;

        // No flat GST columns survive on any invoice spine.
        foreach (var p in new[] { "Cgst", "Sgst", "Igst", "CgstAmount", "IgstAmount" })
            Assert.Null(et.FindProperty(p));

        var nav = et.FindNavigation("Tax");
        Assert.NotNull(nav);
        Assert.True(nav!.TargetEntityType.IsMappedToJson());
        Assert.Equal("tax_breakdown", nav.TargetEntityType.GetContainerColumnName());
    }

    // Minimal fixtures for all 4 invoice tables (orders carries rate+amount; rest amount-only).
    private const string Fixture = """
        CREATE SCHEMA IF NOT EXISTS order_lifecycle;
        CREATE SCHEMA IF NOT EXISTS commerce;
        CREATE SCHEMA IF NOT EXISTS finance_royalty;
        CREATE TABLE order_lifecycle.invoices (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            cgst_rate numeric(5,2) NOT NULL DEFAULT 0, cgst_amount numeric(14,2) NOT NULL DEFAULT 0,
            sgst_rate numeric(5,2) NOT NULL DEFAULT 0, sgst_amount numeric(14,2) NOT NULL DEFAULT 0,
            igst_rate numeric(5,2) NOT NULL DEFAULT 0, igst_amount numeric(14,2) NOT NULL DEFAULT 0);
        CREATE TABLE commerce.subscription_invoices (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            cgst numeric(14,2) NOT NULL DEFAULT 0, sgst numeric(14,2) NOT NULL DEFAULT 0, igst numeric(14,2) NOT NULL DEFAULT 0);
        CREATE TABLE finance_royalty.royalty_invoices (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            cgst numeric(14,2) NOT NULL DEFAULT 0, sgst numeric(14,2) NOT NULL DEFAULT 0, igst numeric(14,2) NOT NULL DEFAULT 0);
        CREATE TABLE finance_royalty.franchise_subscription_invoices (
            id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            cgst numeric(14,2) NOT NULL DEFAULT 0, sgst numeric(14,2) NOT NULL DEFAULT 0, igst numeric(14,2) NOT NULL DEFAULT 0);
        INSERT INTO order_lifecycle.invoices (cgst_rate, cgst_amount, sgst_rate, sgst_amount)
            VALUES (9, 90.00, 9, 90.00);
        INSERT INTO commerce.subscription_invoices (igst) VALUES (50.00);
        INSERT INTO finance_royalty.royalty_invoices (igst) VALUES (75.00);
        INSERT INTO finance_royalty.franchise_subscription_invoices (igst) VALUES (33.00);
        """;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var v = await cmd.ExecuteScalarAsync();
        return v is DBNull ? null : v;
    }

    [Fact]
    public async Task SliceF_moves_all_four_invoice_tables_to_tax_breakdown_atomically()
    {
        if (!_dockerAvailable) return;

        await using var conn = await OpenAsync();
        await ExecAsync(conn, Fixture);
        await ExecAsync(conn, await File.ReadAllTextAsync(RepoPaths.Patch("phase2_slice_f_tax_breakdown.sql")));

        // Orders invoice: rate+amount preserved into the jsonb.
        Assert.Equal(90.00m, Convert.ToDecimal(await ScalarAsync(conn,
            "SELECT (tax_breakdown->>'cgst_amount')::numeric FROM order_lifecycle.invoices LIMIT 1;")));
        Assert.Equal(9m, Convert.ToDecimal(await ScalarAsync(conn,
            "SELECT (tax_breakdown->>'cgst_rate')::numeric FROM order_lifecycle.invoices LIMIT 1;")));

        // Amount-only invoices: igst backfilled to igst_amount, rates 0.
        Assert.Equal(50.00m, Convert.ToDecimal(await ScalarAsync(conn,
            "SELECT (tax_breakdown->>'igst_amount')::numeric FROM commerce.subscription_invoices LIMIT 1;")));
        Assert.Equal(75.00m, Convert.ToDecimal(await ScalarAsync(conn,
            "SELECT (tax_breakdown->>'igst_amount')::numeric FROM finance_royalty.royalty_invoices LIMIT 1;")));
        Assert.Equal(33.00m, Convert.ToDecimal(await ScalarAsync(conn,
            "SELECT (tax_breakdown->>'igst_amount')::numeric FROM finance_royalty.franchise_subscription_invoices LIMIT 1;")));

        // No old GST columns remain on any of the four tables.
        Assert.Equal(0L, (long)(await ScalarAsync(conn, """
            SELECT count(*) FROM information_schema.columns
            WHERE column_name IN ('cgst','sgst','igst','cgst_amount','sgst_amount','igst_amount','cgst_rate','sgst_rate','igst_rate')
              AND ((table_schema='order_lifecycle' AND table_name='invoices')
                OR (table_schema='commerce' AND table_name='subscription_invoices')
                OR (table_schema='finance_royalty' AND table_name IN ('royalty_invoices','franchise_subscription_invoices')));
            """))!);
    }
}
