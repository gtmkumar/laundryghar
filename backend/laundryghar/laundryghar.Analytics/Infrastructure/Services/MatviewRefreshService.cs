using laundryghar.SharedDataModel.Persistence;

namespace laundryghar.Analytics.Infrastructure.Services;

/// <summary>
/// Periodically refreshes the analytics materialized views so the dashboard
/// reflects real order/payment activity without an admin clicking "refresh".
///
/// The matviews are owned by <c>postgres</c> and the service connects as
/// <c>app_user</c>, which cannot REFRESH them directly. We call the
/// <c>analytics.refresh_all_matviews()</c> SECURITY DEFINER function instead
/// (owned by postgres, RLS-exempt so it aggregates across all brands; app_user
/// is granted EXECUTE). See db/patches/analytics_refresh_function.sql.
///
/// Runs once at startup (so a freshly-booted/seeded stack populates immediately)
/// then every <c>Analytics:MatviewRefreshMinutes</c> minutes (default 5). The
/// refresh is CONCURRENTLY inside the function, so dashboard reads are never
/// blocked. A failed cycle is logged and retried on the next tick.
/// </summary>
public sealed class MatviewRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MatviewRefreshService> _logger;
    private readonly TimeSpan _interval;

    public MatviewRefreshService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<MatviewRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        var minutes = config.GetValue<int?>("Analytics:MatviewRefreshMinutes") ?? 5;
        _interval = TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MatviewRefreshService starting (interval={Minutes}m).", _interval.TotalMinutes);

        // Refresh immediately on boot, then on the interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("MatviewRefreshService stopped.");
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            await db.Database.ExecuteSqlRawAsync("SELECT analytics.refresh_all_matviews();", ct);

            _logger.LogDebug("MatviewRefreshService: analytics matviews refreshed.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MatviewRefreshService: refresh cycle failed; will retry next tick.");
        }
    }
}
