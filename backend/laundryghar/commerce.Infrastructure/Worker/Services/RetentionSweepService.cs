using commerce.Infrastructure.Worker.Options;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace commerce.Infrastructure.Worker.Services;

/// <summary>
/// Daily retention sweep that hard-deletes transient rows beyond their configured retention window.
///
/// Targets (app-level deletes — NOT partitioned tables):
///   • engagement_cms.notifications_outbox  — terminal-status rows older than <see cref="WorkerOptions.NotificationOutboxRetentionDays"/> (default 180 d)
///   • identity_access.otp_codes            — expired rows older than <see cref="WorkerOptions.OtpCodeRetentionDays"/> (default 30 d)
///   • identity_access.refresh_tokens       — revoked or expired rows older than <see cref="WorkerOptions.RefreshTokenRetentionDays"/> (default 90 d)
///
/// Partitioned tables (pg_partman manages their retention — leave them alone):
///   • logistics.rider_location_pings  — retention = 14 days, configured.
///   • engagement_cms.notifications_log — partitioned by month; no pg_partman retention currently
///     configured (audit log; kept indefinitely until admin sets a policy).
///   • identity_access.audit_logs       — partitioned monthly; no retention configured.
///
/// The sweep runs once per <see cref="WorkerOptions.RetentionSweepIntervalSeconds"/> (default: daily).
/// Errors within a target are isolated — one failure does not abort the others.
/// </summary>
public sealed class RetentionSweepService : BackgroundService
{
    private readonly IServiceScopeFactory              _scopeFactory;
    private readonly ILogger<RetentionSweepService>    _logger;
    private readonly WorkerOptions                     _options;

    public RetentionSweepService(
        IServiceScopeFactory           scopeFactory,
        ILogger<RetentionSweepService> logger,
        IOptions<WorkerOptions>        options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RetentionSweepService starting (sweepInterval={Interval}s, " +
            "outboxRetentionDays={Outbox}, otpRetentionDays={Otp}, tokenRetentionDays={Token}).",
            _options.RetentionSweepIntervalSeconds,
            _options.NotificationOutboxRetentionDays,
            _options.OtpCodeRetentionDays,
            _options.RefreshTokenRetentionDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RetentionSweepService: unhandled error; will retry next tick.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.RetentionSweepIntervalSeconds),
                stoppingToken);
        }

        _logger.LogInformation("RetentionSweepService stopped.");
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        _logger.LogDebug("RetentionSweepService: sweep cycle starting.");

        await SweepNotificationOutboxAsync(ct);
        await SweepOtpCodesAsync(ct);
        await SweepRefreshTokensAsync(ct);
        await PurgeCancelledBrandsAsync(ct);
        await CheckDomainHealthAsync(ct);

        _logger.LogDebug("RetentionSweepService: sweep cycle complete.");
    }

    /// <summary>
    /// Deletes terminal-status notifications_outbox rows whose created_at is older than
    /// <see cref="WorkerOptions.NotificationOutboxRetentionDays"/> days.
    ///
    /// Terminal statuses: sent, failed, expired, suppressed, cancelled.
    /// Rows in pending/queued/sending are preserved regardless of age.
    /// </summary>
    private async Task SweepNotificationOutboxAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateWorkerAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.NotificationOutboxRetentionDays);

            var terminalStatuses = new[] { "sent", "failed", "expired", "suppressed", "cancelled" };

            var deleted = await db.NotificationOutboxes
                .Where(n => terminalStatuses.Contains(n.Status) && n.CreatedAt < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation(
                    "RetentionSweepService: deleted {Count} notifications_outbox row(s) " +
                    "(terminal, older than {Days} days).",
                    deleted, _options.NotificationOutboxRetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RetentionSweepService: error sweeping notifications_outbox; skipping target.");
        }
    }

    /// <summary>
    /// Deletes otp_codes rows where expires_at is older than
    /// <see cref="WorkerOptions.OtpCodeRetentionDays"/> days.
    ///
    /// Uses expires_at (not created_at) so active OTPs with a far future expiry are never deleted.
    /// </summary>
    private async Task SweepOtpCodesAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateWorkerAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.OtpCodeRetentionDays);

            var deleted = await db.OtpCodes
                .Where(o => o.ExpiresAt < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation(
                    "RetentionSweepService: deleted {Count} otp_codes row(s) " +
                    "(expired more than {Days} day(s) ago).",
                    deleted, _options.OtpCodeRetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RetentionSweepService: error sweeping otp_codes; skipping target.");
        }
    }

    /// <summary>
    /// Deletes refresh_tokens rows that are both inactive (revoked OR expired) and older than
    /// <see cref="WorkerOptions.RefreshTokenRetentionDays"/> days.
    ///
    /// A token is inactive when <c>revoked_at IS NOT NULL</c> OR <c>expires_at &lt; NOW()</c>.
    /// The age cutoff is applied against <c>created_at</c> so recently issued but already-expired
    /// tokens are kept until the window elapses.
    /// </summary>
    private async Task SweepRefreshTokensAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateWorkerAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            var now    = DateTimeOffset.UtcNow;
            var cutoff = now.AddDays(-_options.RefreshTokenRetentionDays);

            var deleted = await db.RefreshTokens
                .Where(t =>
                    t.CreatedAt < cutoff
                    && (t.RevokedAt != null || t.ExpiresAt < now))
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation(
                    "RetentionSweepService: deleted {Count} refresh_tokens row(s) " +
                    "(revoked/expired, older than {Days} days).",
                    deleted, _options.RefreshTokenRetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RetentionSweepService: error sweeping refresh_tokens; skipping target.");
        }
    }

    /// <summary>
    /// §9's last step: "export offered, wind-down retention, then <b>deletion</b> per DPDP."
    ///
    /// <para>Deletes every brand whose retention window has elapsed. Deliberately the ONLY code path
    /// that can destroy a tenant — there is no delete endpoint — because the two properties that
    /// make this safe are both properties of a scheduled job: it cannot be triggered by a mis-click,
    /// and it cannot run early. The window, set when the provider cancelled, is the whole
    /// safeguard.</para>
    ///
    /// <para>Withdrawal is honoured by construction: withdrawing flips the row's status away from
    /// <c>retention</c>, and this only ever selects rows still in it. A provider who changes their
    /// mind at hour 23 of day 30 is simply never picked up.</para>
    ///
    /// <para>Each brand is purged in its own transaction and its own scope. One tenant whose purge
    /// stalls on an unexpected foreign key must not block every other tenant's — and
    /// <c>kernel.purge_brand</c> raises rather than half-finishing, so a stall leaves that brand
    /// exactly as it was, still in retention, to be retried on the next tick and investigated.</para>
    /// </summary>
    private async Task PurgeCancelledBrandsAsync(CancellationToken ct)
    {
        List<Guid> due;
        try
        {
            await using var scope = _scopeFactory.CreateWorkerAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            due = await db.Set<laundryghar.SharedDataModel.Entities.TenancyOrg.BrandCancellation>()
                .Where(c => c.Status == laundryghar.SharedDataModel.Entities.TenancyOrg.BrandCancellationStatus.Retention
                         && c.RetentionUntil <= DateTimeOffset.UtcNow)
                .Select(c => c.BrandId)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionSweepService: could not list brands due for purge.");
            return;
        }

        foreach (var brandId in due)
        {
            try
            {
                await using var scope = _scopeFactory.CreateWorkerAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

                var rows = await db.Database
                    .SqlQuery<long>($"SELECT sum(rows_deleted)::bigint AS \"Value\" FROM kernel.purge_brand({brandId})")
                    .ToListAsync(ct);

                await db.Set<laundryghar.SharedDataModel.Entities.TenancyOrg.BrandCancellation>()
                    .Where(c => c.BrandId == brandId
                             && c.Status == laundryghar.SharedDataModel.Entities.TenancyOrg.BrandCancellationStatus.Retention)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(c => c.Status, laundryghar.SharedDataModel.Entities.TenancyOrg.BrandCancellationStatus.Purged)
                        .SetProperty(c => c.PurgedAt, DateTimeOffset.UtcNow)
                        .SetProperty(c => c.UpdatedAt, DateTimeOffset.UtcNow), ct);

                _logger.LogWarning(
                    "RetentionSweepService: purged brand {BrandId} — {Rows} row(s) deleted after the "
                    + "retention window elapsed. The brand record remains as a tombstone for the audit ledger.",
                    brandId, rows.Count > 0 ? rows[0] : 0);
            }
            catch (Exception ex)
            {
                // Left in 'retention' on purpose: a purge that partially ran and was marked complete
                // would mean telling a customer their data is gone when some of it is not.
                _logger.LogError(ex,
                    "RetentionSweepService: purge FAILED for brand {BrandId}; it stays in retention "
                    + "and will be retried. Investigate before reporting deletion as complete.", brandId);
            }
        }
    }

    /// <summary>
    /// §12: automated SSL and domain health checks "from day one".
    ///
    /// <para>Observes each verified custom domain and records what it found. It does NOT issue or
    /// renew certificates — that half is blocked on OQ-6 (Let's Encrypt vs Cloudflare-for-SaaS), and
    /// choosing a vendor on a customer's behalf is choosing their bill. Checking is identical
    /// whichever wins, and is worth having alone: without it, a custom domain's certificate can
    /// expire and the first anyone hears of it is a customer's customer seeing a browser
    /// warning.</para>
    ///
    /// <para>Stalest-first and capped per tick, so a large estate drains fairly instead of the same
    /// alphabetical prefix being checked every day while the tail is never looked at.</para>
    /// </summary>
    private async Task CheckDomainHealthAsync(CancellationToken ct)
    {
        const int PerTick = 100;

        List<string> domains;
        try
        {
            await using var scope = _scopeFactory.CreateWorkerAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            domains = await db.Database.SqlQuery<string>($"""
                SELECT domain::text AS "Value"
                FROM   tenancy_org.brand_domains
                WHERE  verified_at IS NOT NULL
                ORDER  BY last_checked_at NULLS FIRST
                LIMIT  {PerTick}
                """).ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RetentionSweepService: could not list domains to health-check.");
            return;
        }

        if (domains.Count == 0) return;

        var checker = new laundryghar.SharedDataModel.Persistence.TlsDomainHealthChecker();
        var unhealthy = 0;

        foreach (var domain in domains)
        {
            try
            {
                var result = await checker.CheckAsync(domain, ct);

                await using var scope = _scopeFactory.CreateWorkerAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();
                await db.Database.ExecuteSqlAsync(
                    $"SELECT kernel.record_domain_check({domain}, {result.Health}, {result.Detail}, {result.SslExpiresAt}, {result.Issuer})",
                    ct);

                if (result.Health != laundryghar.SharedDataModel.Contracts.DomainHealth.Healthy)
                {
                    unhealthy++;
                    _logger.LogWarning(
                        "Domain health: {Domain} is {Health} — {Detail}",
                        domain, result.Health, result.Detail ?? "no detail");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One domain's failure must not abandon the rest of the sweep.
                _logger.LogError(ex, "Domain health: check failed for {Domain}.", domain);
            }
        }

        _logger.LogInformation(
            "Domain health: checked {Count} domain(s), {Unhealthy} needing attention.",
            domains.Count, unhealthy);
    }
}
