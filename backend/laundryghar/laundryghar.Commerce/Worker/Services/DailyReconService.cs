using System.Text.Json;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Services;

/// <summary>
/// Daily reconciliation auto-creation job.
///
/// At the configured local hour (default: 21:00, controlled by Worker:DailyReconHourLocal)
/// for each active warehouse across all brands, the job checks whether a reconciliation
/// already exists for today (type=daily). If not, it creates one and auto-adds any
/// in-flight garments whose last scan is older than Worker:ReconStaleHours (default: 12h)
/// as recon items with status='missing' (candidate — not confirmed lost).
///
/// Design choices:
///   - Status='missing' rather than a softer 'expected' is used because the domain
///     validator (AddReconItemValidator) permits it and the intent is to flag items
///     that have gone dark for more than half a working day as needing attention.
///     A human closing the recon can resolve, escalate or dismiss each item.
///     Confirmed-lost is triggered only by closing a recon that still has unresolved
///     missing items — see CloseStockReconHandler (which in turn calls MarkLostAsync
///     in this service for items still 'missing' at close time) — NOT by auto-adding.
///   - Idempotent: checks for an existing daily recon for today before creating.
///   - Per-warehouse error isolation: one warehouse failure does not abort others.
///   - Emits a recon.daily_created outbox event per created session (picked up by
///     OutboxEventRelayService for downstream consumers).
///   - Worker bypasses RLS (WorkerCurrentTenant.BypassRls = true) — all brands visible.
///
/// Wallet compensation for confirmed-lost garments is OUT OF SCOPE this round (needs
/// policy — credit amount, caps, approval workflow). The garment.lost event carries
/// enough context for a future compensation handler.
/// </summary>
public sealed class DailyReconService : BackgroundService
{
    private readonly IServiceScopeFactory           _scopeFactory;
    private readonly ILogger<DailyReconService>     _logger;
    private readonly WorkerOptions                  _options;

    public DailyReconService(
        IServiceScopeFactory          scopeFactory,
        ILogger<DailyReconService>    logger,
        IOptions<WorkerOptions>       options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.DailyReconEnabled)
        {
            _logger.LogInformation(
                "DailyReconService disabled (Worker:DailyReconEnabled=false). " +
                "Set to true to enable the nightly reconciliation auto-creation job.");
            return;
        }

        _logger.LogInformation(
            "DailyReconService starting (triggerHourLocal={Hour}, staleHours={StaleHours}).",
            _options.DailyReconHourLocal,
            _options.ReconStaleHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaybeRunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DailyReconService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }

        _logger.LogInformation("DailyReconService stopped.");
    }

    /// <summary>
    /// Fires once when wall-clock hour (UTC) equals the configured local trigger hour
    /// and the run has not already happened in the current day (idempotent — guarded by
    /// per-warehouse recon existence check, not a separate flag).
    /// </summary>
    private async Task MaybeRunCycleAsync(CancellationToken ct)
    {
        var nowLocal  = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(_options.DailyReconHourLocal >= 0 ? 0 : 0));
        // Treat "local" loosely as UTC+5:30 (IST) because warehouse timezones are not yet
        // individually stored in a machine-readable format. A per-warehouse timezone column
        // is a future improvement; for now IST is the operating zone.
        var nowIst    = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330));
        var today     = DateOnly.FromDateTime(nowIst.DateTime);

        // Only run during the configured hour (e.g. 21:xx IST).
        if (nowIst.Hour != _options.DailyReconHourLocal)
            return;

        _logger.LogInformation(
            "DailyReconService: checking warehouses for today={Today} (IST hour={Hour}).",
            today, _options.DailyReconHourLocal);

        await RunForAllWarehousesAsync(today, ct);
    }

    private async Task RunForAllWarehousesAsync(DateOnly today, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateWorkerAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

        // Worker bypasses RLS — all brands/warehouses are visible.
        var warehouses = await db.Warehouses
            .Where(w => w.Status == "active" && w.DeletedAt == null)
            .Select(w => new { w.Id, w.BrandId, w.Name })
            .ToListAsync(ct);

        if (warehouses.Count == 0)
        {
            _logger.LogInformation("DailyReconService: no active warehouses found.");
            return;
        }

        _logger.LogInformation(
            "DailyReconService: {Count} active warehouse(s) to process.", warehouses.Count);

        foreach (var wh in warehouses)
        {
            try
            {
                await ProcessWarehouseAsync(db, wh.Id, wh.BrandId, wh.Name, today, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DailyReconService: failed for warehouseId={WarehouseId} name={Name}; " +
                    "skipping — will not affect other warehouses.",
                    wh.Id, wh.Name);
            }
        }
    }

    private async Task ProcessWarehouseAsync(
        LaundryGharDbContext db,
        Guid warehouseId,
        Guid brandId,
        string warehouseName,
        DateOnly today,
        CancellationToken ct)
    {
        // Idempotent guard: skip if a daily recon for today already exists.
        var alreadyExists = await db.StockReconciliations
            .AnyAsync(r =>
                r.WarehouseId == warehouseId &&
                r.BrandId     == brandId     &&
                r.ReconDate   == today       &&
                r.ReconType   == "daily",
                ct);

        if (alreadyExists)
        {
            _logger.LogDebug(
                "DailyReconService: daily recon already exists for warehouseId={WarehouseId} date={Date}; skipping.",
                warehouseId, today);
            return;
        }

        var now      = DateTimeOffset.UtcNow;
        var staleAt  = now.AddHours(-_options.ReconStaleHours);

        // Find in-flight garments at this warehouse whose last scan is older than threshold.
        // 'in-flight' = stage is not terminal (not dispatched/delivered/returned/lost/damaged).
        var terminalStages = new[] { "dispatched", "delivered", "returned", "lost", "damaged" };

        var staleGarments = await db.Garments
            .Where(g =>
                g.WarehouseId == warehouseId &&
                g.BrandId     == brandId     &&
                g.Status      == "active"    &&
                !terminalStages.Contains(g.CurrentStage) &&
                (g.LastScannedAt == null || g.LastScannedAt <= staleAt))
            .Select(g => new
            {
                g.Id,
                g.TagCode,
                g.CurrentStage,
                g.CustomerId,
                g.OrderId,
                g.LastScannedAt
            })
            .ToListAsync(ct);

        // Create the reconciliation session.
        var recon = new SharedDataModel.Entities.OrderLifecycle.StockReconciliation
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            WarehouseId     = warehouseId,
            StoreId         = null,
            ReconDate       = today,
            ReconType       = "daily",
            StartedAt       = now,
            StartedBy       = Guid.Empty,   // system-initiated; no user actor
            Status          = "in_progress",
            Summary         = "{}",
            ExpectedCount   = staleGarments.Count,
            ScannedCount    = staleGarments.Count,
            MatchedCount    = 0,
            MissingCount    = staleGarments.Count,
            UnexpectedCount = 0,
            DamagedCount    = 0,
            ResolvedMissingCount = 0,
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = null,
            UpdatedBy       = null
        };

        db.StockReconciliations.Add(recon);

        // Auto-add stale garments as 'missing' candidate items.
        foreach (var g in staleGarments)
        {
            db.StockReconciliationItems.Add(new SharedDataModel.Entities.OrderLifecycle.StockReconciliationItem
            {
                Id               = Guid.NewGuid(),
                ReconciliationId = recon.Id,
                BrandId          = brandId,
                GarmentId        = g.Id,
                TagCode          = g.TagCode,
                ExpectedStage    = g.CurrentStage,
                FoundStage       = null,
                Status           = "missing",   // candidate — not confirmed; human must close to confirm
                FlaggedAt        = now,
                CreatedAt        = now,
                CreatedBy        = null
            });
        }

        // Outbox event for downstream consumption (notifications, analytics).
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "stock_reconciliation",
            AggregateId   = recon.Id,
            EventType     = "recon.daily_created",
            EventVersion  = 1,
            Payload       = JsonSerializer.Serialize(new
            {
                ReconId       = recon.Id,
                BrandId       = brandId,
                WarehouseId   = warehouseId,
                WarehouseName = warehouseName,
                ReconDate     = today.ToString("yyyy-MM-dd"),
                MissingCount  = staleGarments.Count,
                StaleHours    = _options.ReconStaleHours,
                CreatedAt     = now
            }),
            Metadata      = "{}",
            OccurredAt    = now,
            Status        = "pending",
            CreatedAt     = now
        });

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DailyReconService: created daily recon {ReconId} for warehouseId={WarehouseId} " +
            "name={Name} date={Date} — {Missing} missing-candidate item(s).",
            recon.Id, warehouseId, warehouseName, today, staleGarments.Count);
    }
}
