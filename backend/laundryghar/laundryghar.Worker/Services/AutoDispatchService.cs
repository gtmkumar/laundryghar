using System.Text.Json;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Logistics;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Worker.Options;
using laundryghar.Worker.Services.AutoDispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace laundryghar.Worker.Services;

/// <summary>
/// Auto-dispatch background service.
///
/// Each poll cycle it finds pending <c>order_lifecycle.pickup_requests</c> that have no
/// <c>delivery_assignment</c> yet (status='pending', older than <see cref="WorkerOptions.AutoDispatchMinAgeMinutes"/>),
/// ranks eligible riders in the same brand by (CurrentLoad asc, Haversine distance asc),
/// creates a <c>delivery_assignment</c> row (leg_type=pickup, status=assigned), bumps
/// <c>current_load</c>, and emits an <c>assignment.auto_assigned</c> outbox event.
///
/// Safety:
///   - Disabled by default (<c>AutoDispatch:Enabled=false</c>). Enable explicitly.
///   - Worker bypasses RLS; every query is scoped by brand_id explicitly.
///   - Per-cycle error isolation — one bad pickup does not abort the whole batch.
///   - Idempotent pickup check: skips any pickup_request that already has an assignment.
/// </summary>
public sealed class AutoDispatchService : BackgroundService
{
    private readonly IServiceScopeFactory            _scopeFactory;
    private readonly ILogger<AutoDispatchService>    _logger;
    private readonly WorkerOptions                   _options;

    public AutoDispatchService(
        IServiceScopeFactory         scopeFactory,
        ILogger<AutoDispatchService> logger,
        IOptions<WorkerOptions>      options)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _options      = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoDispatchEnabled)
        {
            _logger.LogInformation(
                "AutoDispatchService disabled (AutoDispatch:Enabled=false). Set to true to enable.");
            return;
        }

        _logger.LogInformation(
            "AutoDispatchService starting " +
            "(pollSeconds={Poll}, minAgeMinutes={MinAge}, maxPerCycle={MaxPerCycle}).",
            _options.AutoDispatchPollSeconds,
            _options.AutoDispatchMinAgeMinutes,
            _options.AutoDispatchMaxPerCycle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AutoDispatchService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.AutoDispatchPollSeconds),
                stoppingToken);
        }

        _logger.LogInformation("AutoDispatchService stopped.");
    }

    private async Task ProcessCycleAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

        var minAge = DateTimeOffset.UtcNow.AddMinutes(-_options.AutoDispatchMinAgeMinutes);

        // Fetch candidate pickup requests:
        //   - status = 'pending'
        //   - older than MinAgeMinutes (so admins can beat the job for fresh requests)
        //   - no delivery_assignment yet (any leg_type — idempotency guard)
        // Worker bypasses RLS, so we get all brands. Cross-brand isolation is maintained
        // by grouping work per brand_id when selecting riders.
        // Project the NTS Point object itself rather than its .Y/.X components.
        // EF/Npgsql translates ST_Y(geography) → PostgresException 42883 because ST_Y
        // only accepts geometry, not geography. Materialising the Point client-side avoids
        // any server-side coordinate-extraction expression; we read .Y/.X in memory below.
        var pendingPickups = await db.PickupRequests
            .Where(p => p.Status == "pending"
                     && p.CreatedAt <= minAge
                     && !db.DeliveryAssignments
                           .Any(a => a.PickupRequestId == p.Id))
            .OrderBy(p => p.CreatedAt)
            .Take(_options.AutoDispatchMaxPerCycle)
            .Select(p => new
            {
                p.Id,
                p.BrandId,
                p.StoreId,
                p.FranchiseId,
                p.CreatedAt,
                AddressPoint = p.Address.GeoLocation   // NTS Point; null when no geo
            })
            .ToListAsync(ct);

        if (pendingPickups.Count == 0)
            return;

        _logger.LogInformation(
            "AutoDispatchService: found {Count} unassigned pickup request(s).",
            pendingPickups.Count);

        // Load eligible riders per brand to avoid redundant queries.
        // Eligible: is_on_duty=true, status='active', current_load < daily_delivery_capacity.
        var brandIds = pendingPickups.Select(p => p.BrandId).Distinct().ToList();

        // Same geography fix: project the Point, read .Y/.X in memory after materialisation.
        var ridersByBrand = await db.Riders
            .Where(r => brandIds.Contains(r.BrandId)
                     && r.IsOnDuty
                     && r.Status == "active"
                     && r.CurrentLoad < r.DailyDeliveryCapacity
                     && r.DeletedAt == null)
            .Select(r => new
            {
                r.Id,
                r.BrandId,
                r.FranchiseId,
                r.PrimaryStoreId,
                r.CurrentLoad,
                r.DailyDeliveryCapacity,
                LocationPoint = r.LastKnownLocation   // NTS Point; null when no recent ping
            })
            .ToListAsync(ct);

        int assigned = 0;

        foreach (var pickup in pendingPickups)
        {
            try
            {
                // Filter candidates to the same brand; prefer franchise match when available.
                var brandRiders = ridersByBrand
                    .Where(r => r.BrandId == pickup.BrandId)
                    .ToList();

                // Narrow to same franchise when determinable (franchise-scoped assignments).
                var franchiseScopedRiders = pickup.FranchiseId.HasValue
                    ? brandRiders.Where(r => r.FranchiseId == pickup.FranchiseId.Value).ToList()
                    : brandRiders;

                var candidatePool = franchiseScopedRiders.Count > 0
                    ? franchiseScopedRiders
                    : brandRiders; // fallback: any rider in the brand

                // Read lat/lng from the materialised NTS Point (safe client-side .Y/.X).
                var candidates = candidatePool
                    .Select(r => new RiderCandidate(r.Id, r.CurrentLoad, r.DailyDeliveryCapacity,
                                                    r.LocationPoint?.Y,   // latitude
                                                    r.LocationPoint?.X))  // longitude
                    .ToList();

                var best = RiderRanker.PickBest(
                    candidates,
                    pickup.AddressPoint?.Y,   // latitude (null → distance ranking skipped)
                    pickup.AddressPoint?.X);  // longitude
                if (best is null)
                {
                    _logger.LogDebug(
                        "AutoDispatchService: no eligible rider for pickupRequestId={Id} (brand={Brand}).",
                        pickup.Id, pickup.BrandId);
                    continue;
                }

                // Resolve a real store_id (NOT NULL FK): pickup's StoreId first, then the
                // assigned rider's PrimaryStoreId. If neither is available, skip with a warning
                // rather than inserting Guid.Empty and causing an FK violation.
                var riderSnap = ridersByBrand.First(r => r.Id == best.RiderId);
                var resolvedStoreId = pickup.StoreId
                                   ?? riderSnap.PrimaryStoreId;
                if (!resolvedStoreId.HasValue)
                {
                    _logger.LogWarning(
                        "AutoDispatchService: cannot resolve store_id for pickupRequestId={Id} " +
                        "(pickup.StoreId=null, rider.PrimaryStoreId=null); skipping.",
                        pickup.Id);
                    continue;
                }

                await AssignPickupAsync(db, pickup.Id, pickup.BrandId, resolvedStoreId.Value, best.RiderId, ct);
                assigned++;

                // Update the in-memory rider snapshot so subsequent pickups in this cycle
                // see the incremented load (avoids over-assigning the same rider in one tick).
                var snapshot = ridersByBrand.First(r => r.Id == best.RiderId);
                var updated  = new
                {
                    snapshot.Id,
                    snapshot.BrandId,
                    snapshot.FranchiseId,
                    snapshot.PrimaryStoreId,
                    CurrentLoad           = snapshot.CurrentLoad + 1,
                    snapshot.DailyDeliveryCapacity,
                    snapshot.LocationPoint
                };
                ridersByBrand.Remove(snapshot);
                // Re-add only if still under capacity.
                if (updated.CurrentLoad < updated.DailyDeliveryCapacity)
                    ridersByBrand.Add(updated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AutoDispatchService: failed to assign pickupRequestId={Id}; skipping.",
                    pickup.Id);
            }
        }

        if (assigned > 0)
            _logger.LogInformation(
                "AutoDispatchService: auto-assigned {Count} pickup request(s) this cycle.",
                assigned);
    }

    /// <summary>
    /// Creates a delivery_assignment for the given pickup request and rider, stamps the
    /// pickup_request status to 'assigned', increments current_load, and emits an outbox
    /// event — all in a single transaction.
    /// </summary>
    private static async Task AssignPickupAsync(
        LaundryGharDbContext db,
        Guid  pickupRequestId,
        Guid  brandId,
        Guid  storeId,          // pre-resolved; never Guid.Empty
        Guid  riderId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Re-check under a fresh load: guard against a concurrent manual assignment that
        // happened between the initial fetch and this call.
        var alreadyAssigned = await db.DeliveryAssignments
            .AnyAsync(a => a.PickupRequestId == pickupRequestId, ct);
        if (alreadyAssigned) return;

        var pr = await db.PickupRequests
            .FirstOrDefaultAsync(p => p.Id == pickupRequestId && p.BrandId == brandId, ct);
        if (pr is null || pr.Status != "pending") return;

        var assignment = new DeliveryAssignment
        {
            Id              = Guid.NewGuid(),
            BrandId         = brandId,
            StoreId         = storeId,   // pre-resolved; caller guarantees non-empty
            RiderId         = riderId,
            PickupRequestId = pickupRequestId,
            LegType         = "pickup",
            AssignedAt      = now,
            AssignedBy      = null,          // system-assigned
            AddressSnapshot = "{}",
            OtpVerified     = false,
            Status          = "assigned",
            Metadata        = "{}",
            CreatedAt       = now,
            UpdatedAt       = now,
            CreatedBy       = null,
            UpdatedBy       = null
        };

        pr.Status    = "assigned";
        pr.UpdatedAt = now;

        var payload = JsonSerializer.Serialize(new
        {
            AssignmentId    = assignment.Id,
            PickupRequestId = pickupRequestId,
            BrandId         = brandId,
            RiderId         = riderId,
            AssignedAt      = now,
            Source          = "auto_dispatch"
        });

        var outboxEvent = new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "delivery_assignment",
            AggregateId   = assignment.Id,
            EventType     = "assignment.auto_assigned",
            EventVersion  = 1,
            Payload       = payload,
            Metadata      = "{}",
            OccurredAt    = now,
            Status        = "pending",
            CreatedAt     = now,
            CreatedBy     = null
        };

        db.DeliveryAssignments.Add(assignment);
        db.OutboxEvents.Add(outboxEvent);
        await db.SaveChangesAsync(ct);

        // Increment current_load AFTER the transaction commits.
        await RiderLoadHelper.IncrementAsync(db, riderId, ct);
    }
}
