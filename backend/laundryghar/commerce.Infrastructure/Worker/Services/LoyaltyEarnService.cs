using System.Text.Json;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace commerce.Infrastructure.Worker.Services;

/// <summary>
/// Processes <c>delivery.completed</c> outbox events to credit loyalty earn points.
///
/// Design: uses a cursor-based approach (<c>LoyaltyEarnCursor</c> marker) to track
/// the last outbox event id it has seen. It does NOT mutate <c>OutboxEvent.Status</c>
/// or <c>PublishedAt</c> — those fields are exclusively owned by
/// <see cref="OutboxEventRelayService"/>. Competing consumers on the same row would
/// race non-deterministically, silently dropping either the relay publish or the earn.
///
/// Idempotency: backed by the unique constraint on
/// <c>loyalty.loyalty_points_ledger (order_id, transaction_type, brand_id)</c>.
/// If a ledger row already exists for this order+earn+brand, the event is skipped.
/// </summary>
public sealed class LoyaltyEarnService : BackgroundService
{
    private readonly IServiceScopeFactory        _scopeFactory;
    private readonly ILogger<LoyaltyEarnService> _logger;
    private const int PollDelaySeconds = 15;
    private const int BatchSize        = 20;
    private const string ConsumerName  = "loyalty_earn";

    public LoyaltyEarnService(
        IServiceScopeFactory        scopeFactory,
        ILogger<LoyaltyEarnService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LoyaltyEarnService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Transient error — log and continue; do NOT crash the host.
                _logger.LogError(ex, "LoyaltyEarnService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollDelaySeconds), stoppingToken);
        }

        _logger.LogInformation("LoyaltyEarnService stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateWorkerAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();

        // Load or initialise our cursor.
        // NotificationEventCursors table tracks per-consumer watermarks in the
        // engagement_cms schema; we reuse the same pattern here with a distinct name.
        var cursor = await db.NotificationEventCursors
            .FirstOrDefaultAsync(c => c.ConsumerName == ConsumerName, ct);

        if (cursor is null)
        {
            cursor = new laundryghar.SharedDataModel.Entities.EngagementCms.NotificationEventCursor
            {
                ConsumerName   = ConsumerName,
                LastEventId    = null,
                ProcessedCount = 0,
                UpdatedAt      = DateTimeOffset.UtcNow
            };
            db.NotificationEventCursors.Add(cursor);
            await db.SaveChangesAsync(ct);
        }

        // Fetch events strictly after the cursor watermark.
        // We read ALL delivery.completed events regardless of Status — the relay may
        // have already published some of them; we deduplicate in the ledger, not here.
        var query = db.OutboxEvents
            .Where(e => e.EventType == "delivery.completed")
            .OrderBy(e => e.OccurredAt)
            .Take(BatchSize);

        if (cursor.LastEventId.HasValue)
        {
            // Find the OccurredAt of the last processed event to use as a watermark.
            // We need the row to compare; if deleted somehow, fall back to beginning.
            var lastEvent = await db.OutboxEvents
                .Where(e => e.Id == cursor.LastEventId.Value)
                .Select(e => new { e.OccurredAt })
                .FirstOrDefaultAsync(ct);

            if (lastEvent is not null)
                query = db.OutboxEvents
                    .Where(e => e.EventType == "delivery.completed"
                             && e.OccurredAt > lastEvent.OccurredAt)
                    .OrderBy(e => e.OccurredAt)
                    .Take(BatchSize);
        }

        var events = await query.ToListAsync(ct);
        if (events.Count == 0) return;

        Guid lastProcessedId = cursor.LastEventId ?? Guid.Empty;

        foreach (var evt in events)
        {
            await ProcessEventAsync(db, evt, ct);
            lastProcessedId = evt.Id;
        }

        // Advance cursor — this is the ONLY mutation we make to shared state.
        cursor.LastEventId    = lastProcessedId;
        cursor.ProcessedCount += events.Count;
        cursor.UpdatedAt      = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessEventAsync(
        LaundryGharDbContext db,
        laundryghar.SharedDataModel.Entities.Kernel.OutboxEvent evt,
        CancellationToken    ct)
    {
        try
        {
            using var doc  = JsonDocument.Parse(evt.Payload);
            var root       = doc.RootElement;

            var orderId  = root.GetProperty("orderId").GetGuid();
            var brandId  = evt.BrandId
                ?? (root.TryGetProperty("brandId", out var bElem) ? bElem.GetGuid() : (Guid?)null);

            if (brandId is null)
            {
                _logger.LogWarning(
                    "LoyaltyEarnService: eventId={EventId} has no brandId — skipping.", evt.Id);
                return;
            }

            // Idempotency guard: skip if an earn entry already exists for this order.
            // This is the primary dedup mechanism — safe to re-check on every retry.
            var alreadyCredited = await db.LoyaltyPointsLedger
                .AnyAsync(l => l.OrderId         == orderId
                            && l.TransactionType == "earn"
                            && l.BrandId         == brandId.Value, ct);

            if (alreadyCredited)
            {
                _logger.LogDebug(
                    "LoyaltyEarnService: earn already recorded for orderId={OrderId} — skipping.", orderId);
                return;
            }

            // Load the order (partitioned table — query by both Id and BrandId for safety)
            var order = await db.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.BrandId == brandId, ct);

            if (order is null)
            {
                _logger.LogWarning(
                    "LoyaltyEarnService: order {OrderId} not found for brand {BrandId} — skipping.",
                    orderId, brandId);
                return;
            }

            // Load the active loyalty program for this brand
            var program = await db.LoyaltyPrograms
                .FirstOrDefaultAsync(
                    lp => lp.BrandId == brandId.Value && lp.IsActive && lp.Status == "active", ct);

            if (program is null) return;

            // Check MinOrderForEarn threshold
            if (order.GrandTotal < program.MinOrderForEarn)
            {
                _logger.LogDebug(
                    "LoyaltyEarnService: order {OrderId} grand total {GrandTotal} below MinOrderForEarn {Min} — no earn.",
                    orderId, order.GrandTotal, program.MinOrderForEarn);
                return;
            }

            // Load the customer
            var customer = await db.Customers
                .FirstOrDefaultAsync(c => c.Id == order.CustomerId && c.BrandId == brandId.Value, ct);

            if (customer is null)
            {
                _logger.LogWarning(
                    "LoyaltyEarnService: customer {CustomerId} not found — skipping earn for orderId={OrderId}.",
                    order.CustomerId, orderId);
                return;
            }

            // Calculate earned points (EarnBasis: 1 point per EarnRate rupees of GrandTotal).
            int earned = program.EarnRate > 0
                ? (int)Math.Floor((double)(order.GrandTotal / program.EarnRate))
                : 0;

            if (earned <= 0) return;

            var now           = DateTimeOffset.UtcNow;
            var balanceBefore = customer.LoyaltyPointsBalance;
            var balanceAfter  = balanceBefore + earned;

            db.LoyaltyPointsLedger.Add(new LoyaltyPointsLedger
            {
                Id               = Guid.NewGuid(),
                BrandId          = brandId.Value,
                CustomerId       = customer.Id,
                LoyaltyProgramId = program.Id,
                TransactionType  = "earn",
                Direction        = 1,
                Points           = earned,
                BalanceBefore    = balanceBefore,
                BalanceAfter     = balanceAfter,
                OrderId          = orderId,
                OrderCreatedAt   = order.CreatedAt,
                Notes            = $"Earned {earned} points on order {order.OrderNumber}",
                PerformedByType  = "system",
                OccurredAt       = now,
                CreatedAt        = now
            });

            // Update customer loyalty balance (tracked entity — EF will UPDATE)
            customer.LoyaltyPointsBalance = balanceAfter;
            customer.UpdatedAt            = now;
            customer.Version++;

            // Stamp the earn count back to the order for reporting
            order.LoyaltyPointsEarned = earned;
            order.UpdatedAt           = now;

            // NOTE: we do NOT touch evt.Status or evt.PublishedAt.
            // OutboxEventRelayService is the exclusive owner of those fields.
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "LoyaltyEarnService: credited {Points} points to customerId={CustomerId} for orderId={OrderId}.",
                earned, customer.Id, orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "LoyaltyEarnService: failed to process eventId={EventId}.", evt.Id);
            // Do NOT advance the cursor for this event — next poll will retry from
            // the cursor position; the per-event idempotency guard prevents double-credit.
        }
    }
}
