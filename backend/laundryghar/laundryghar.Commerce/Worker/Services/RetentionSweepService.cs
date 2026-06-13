using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Services;

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
}
