using commerce.Infrastructure.Worker.Options;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace commerce.Infrastructure.Worker.Services;

/// <summary>
/// DEFECT 5b — keeps the daily partitions of <c>logistics.rider_location_pings</c>
/// provisioned ahead of time so rider GPS pings always have a partition to land in.
///
/// Root cause this fixes: there was previously NO partition-maintenance job at all.
/// The seeded daily partitions ran out, and new pings either fell into the catch-all
/// DEFAULT partition or failed outright — surfacing to the rider app as a 400 on
/// POST /api/v1/rider/location/ping.
///
/// Like <c>MatviewRefreshService</c>, the table is owned by <c>postgres</c> and this
/// worker connects as <c>app_user</c> (no CREATE on the logistics schema). We therefore
/// call the SECURITY DEFINER function <c>logistics.ensure_rider_ping_partitions(days)</c>
/// (owned by postgres, app_user granted EXECUTE only) rather than issuing DDL directly.
/// See db/patches/rider_ping_partition_maintenance.sql.
///
/// Runs once at startup (so a freshly-booted stack is immediately provisioned) then
/// every <see cref="WorkerOptions.PartitionMaintenanceIntervalSeconds"/> (default daily).
/// The function is idempotent; a failed cycle is logged and retried on the next tick.
/// Opt-out via <c>Worker:PartitionMaintenanceEnabled=false</c>.
/// </summary>
public sealed class PartitionMaintenanceService : BackgroundService
{
    private readonly IServiceScopeFactory             _scopeFactory;
    private readonly ILogger<PartitionMaintenanceService> _logger;
    private readonly WorkerOptions                    _options;

    public PartitionMaintenanceService(
        IServiceScopeFactory                  scopeFactory,
        ILogger<PartitionMaintenanceService>  logger,
        IOptions<WorkerOptions>               options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PartitionMaintenanceEnabled)
        {
            _logger.LogInformation(
                "PartitionMaintenanceService disabled (Worker:PartitionMaintenanceEnabled=false).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(60, _options.PartitionMaintenanceIntervalSeconds));
        _logger.LogInformation(
            "PartitionMaintenanceService starting (interval={Interval}s, daysAhead={Days}).",
            interval.TotalSeconds, _options.PartitionMaintenanceDaysAhead);

        // Provision immediately on boot, then on the interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PartitionMaintenanceService stopped.");
    }

    /// <summary>
    /// Pure planner — the daily partition names that should exist for the window
    /// [today, today+daysAhead] (store-local). Mirrors the naming the SECURITY DEFINER
    /// function uses (<c>rider_location_pings_pYYYYMMDD</c>). Unit-testable without a DB.
    /// </summary>
    internal static IReadOnlyList<string> PlanPartitionNames(DateOnly today, int daysAhead)
    {
        var days = Math.Max(0, daysAhead);
        var names = new List<string>(days + 1);
        for (var i = 0; i <= days; i++)
            names.Add($"rider_location_pings_p{today.AddDays(i):yyyyMMdd}");
        return names;
    }

    /// <summary>One maintenance cycle — idempotently provisions upcoming daily partitions.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateWorkerScope();
            var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

            var daysAhead = Math.Max(0, _options.PartitionMaintenanceDaysAhead);

            // SECURITY DEFINER function returns the count of partitions it created.
            // FormattableString interpolation → a parameterised query (no SQL injection).
            var created = await db.Database
                .SqlQuery<int>(
                    $"SELECT logistics.ensure_rider_ping_partitions({daysAhead}) AS \"Value\"")
                .SingleAsync(ct);

            if (created > 0)
                _logger.LogInformation(
                    "PartitionMaintenanceService: created {Count} rider-ping partition(s).", created);
            else
                _logger.LogDebug(
                    "PartitionMaintenanceService: all rider-ping partitions already provisioned.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PartitionMaintenanceService: cycle failed; will retry next tick.");
        }
    }
}
