using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Identity.Infrastructure.BackgroundServices;

/// <summary>
/// Hourly background sweep that removes expired or abandoned OAuth rows from
/// <c>identity_access.oauth_authorization_codes</c> and <c>identity_access.oauth_clients</c>.
///
/// Targets:
///   • <c>oauth_authorization_codes</c> — rows where <c>expires_at &lt; now() - interval '1 day'</c>
///     (codes expire in 5 minutes; the 1-day grace period keeps recent rows for forensic logs).
///   • <c>oauth_clients</c> — dynamically-registered clients that have <c>last_used_at IS NULL</c>
///     and were created more than 7 days ago (abandoned registrations that never completed a token
///     exchange — abuse/probe registrations caught by the 3/hour rate-limit policy).
///
/// Errors within a target are isolated — one failure does not abort the other.
/// Uses <see cref="IServiceScopeFactory"/> to obtain a scoped <see cref="LaundryGharDbContext"/>
/// per sweep cycle (BackgroundService is a singleton; DbContext is scoped).
/// </summary>
public sealed class OAuthCleanupService : BackgroundService
{
    private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory         _scopeFactory;
    private readonly ILogger<OAuthCleanupService> _logger;

    public OAuthCleanupService(
        IServiceScopeFactory         scopeFactory,
        ILogger<OAuthCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OAuthCleanupService starting (interval={Interval}h).",
            _interval.TotalHours);

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
                    "OAuthCleanupService: unhandled error; will retry next tick.");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("OAuthCleanupService stopped.");
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        _logger.LogDebug("OAuthCleanupService: sweep cycle starting.");

        await SweepExpiredCodesAsync(ct);
        await SweepStaleClientsAsync(ct);

        _logger.LogDebug("OAuthCleanupService: sweep cycle complete.");
    }

    /// <summary>
    /// Deletes <c>oauth_authorization_codes</c> rows where
    /// <c>expires_at &lt; now() - interval '1 day'</c>.
    ///
    /// Codes expire in 5 minutes; the 1-day grace period allows forensic queries to correlate
    /// recent code-exchange failures without retaining rows indefinitely.
    /// </summary>
    private async Task SweepExpiredCodesAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-1);

            var deleted = await db.OAuthAuthorizationCodes
                .Where(c => c.ExpiresAt < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation(
                    "OAuthCleanupService: deleted {Count} oauth_authorization_codes row(s) " +
                    "(expired more than 1 day ago).",
                    deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "OAuthCleanupService: error sweeping oauth_authorization_codes; skipping target.");
        }
    }

    /// <summary>
    /// Deletes <c>oauth_clients</c> rows where <c>last_used_at IS NULL</c> and
    /// <c>created_at &lt; now() - 7 days</c>.
    ///
    /// These are clients that registered but never completed a token exchange — likely
    /// abandoned or probe registrations that slipped through the 3/hour rate-limit policy.
    /// Clients with any successful token exchange (<c>last_used_at IS NOT NULL</c>) are retained.
    /// </summary>
    private async Task SweepStaleClientsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-7);

            var deleted = await db.OAuthClients
                .Where(c => c.LastUsedAt == null && c.CreatedAt < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation(
                    "OAuthCleanupService: deleted {Count} oauth_clients row(s) " +
                    "(last_used_at IS NULL, older than 7 days — abandoned registrations).",
                    deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "OAuthCleanupService: error sweeping stale oauth_clients; skipping target.");
        }
    }
}
