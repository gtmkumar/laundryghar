using System.Text.Json;
using commerce.Application.Commerce.Partner.Wallet;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Exceptions;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace commerce.Infrastructure.Worker.Services;

/// <summary>
/// Consumes <c>partner_booking.debit_wallet</c> outbox events (produced atomically with the booking
/// insert by <c>CreatePartnerBookingHandler</c> in the operations host) and dispatches
/// <see cref="DebitPartnerWalletCommand"/> to draw the quoted fare from the partner's prepaid wallet.
///
/// This closes the RaaS prepaid loop across bounded contexts: the booking lives in operations, the
/// wallet in commerce — both over the one physical LaundryGharDbContext, so the outbox row written
/// by the operations host is visible to this commerce-host worker (same DB, transactional outbox).
///
/// Design (mirrors <see cref="LoyaltyEarnService"/> — the established outbox-consumer template):
///   - Cursor-based watermark (<c>NotificationEventCursor</c>, consumer_name = 'partner_booking_debit')
///     tracks the last processed event. We do NOT mutate <c>OutboxEvent.Status</c>/<c>PublishedAt</c> —
///     those are exclusively owned by <see cref="OutboxEventRelayService"/> (the generic broker relay
///     also drains these rows; the two consumers are independent and never race, because they touch
///     disjoint state).
///   - Idempotency: <see cref="DebitPartnerWalletCommand"/> keys the ledger row on the booking id
///     (unique on partner_wallet_transactions.idempotency_key), so a redelivered event is a no-op.
///     This is the AUTHORITATIVE double-debit guard — the booking-time pre-check only narrows the race.
///   - Failure path: an insufficient-balance <see cref="BusinessRuleException"/> (a race that slipped
///     past the pre-check, or a frozen wallet) is terminal — retrying cannot succeed — so we cancel the
///     unfunded booking (Status → 'cancelled') and advance past the event. Only UNEXPECTED (transient)
///     errors leave the cursor unadvanced so the next poll retries.
///   - Mandatory (no opt-in flag): prepaid economics must always run, else bookings are created but
///     never charged.
/// </summary>
public sealed class PartnerBookingDebitService : BackgroundService
{
    /// <summary>Contract shared with the operations-host producer (CreatePartnerBookingHandler.DebitWalletEventType).</summary>
    private const string EventType     = "partner_booking.debit_wallet";
    private const string ConsumerName  = "partner_booking_debit";
    private const int    PollDelaySeconds = 10;
    private const int    BatchSize        = 25;

    private readonly IServiceScopeFactory                _scopeFactory;
    private readonly ILogger<PartnerBookingDebitService> _logger;

    public PartnerBookingDebitService(
        IServiceScopeFactory                scopeFactory,
        ILogger<PartnerBookingDebitService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PartnerBookingDebitService started.");

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
                _logger.LogError(ex, "PartnerBookingDebitService: unhandled error in poll cycle; will retry next tick.");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollDelaySeconds), stoppingToken);
        }

        _logger.LogInformation("PartnerBookingDebitService stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateWorkerAsyncScope();
        var db         = scope.ServiceProvider.GetRequiredService<LaundryGharDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        // Load or initialise our cursor (per-consumer watermark).
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

        // Fetch debit events strictly after the cursor watermark, oldest first.
        var query = db.OutboxEvents
            .Where(e => e.EventType == EventType)
            .OrderBy(e => e.OccurredAt)
            .Take(BatchSize);

        if (cursor.LastEventId.HasValue)
        {
            var lastEvent = await db.OutboxEvents
                .Where(e => e.Id == cursor.LastEventId.Value)
                .Select(e => new { e.OccurredAt })
                .FirstOrDefaultAsync(ct);

            if (lastEvent is not null)
                query = db.OutboxEvents
                    .Where(e => e.EventType == EventType && e.OccurredAt > lastEvent.OccurredAt)
                    .OrderBy(e => e.OccurredAt)
                    .Take(BatchSize);
        }

        var events = await query.ToListAsync(ct);
        if (events.Count == 0) return;

        Guid lastProcessedId = cursor.LastEventId ?? Guid.Empty;
        int  handledCount    = 0;

        foreach (var evt in events)
        {
            var handled = await ProcessEventAsync(db, dispatcher, evt, ct);
            if (!handled)
                break; // transient failure — stop; next poll retries from the same watermark.

            lastProcessedId = evt.Id;
            handledCount++;
        }

        if (handledCount == 0) return;

        // Advance cursor only past contiguously-handled events (never past an unhandled one).
        cursor.LastEventId    = lastProcessedId;
        cursor.ProcessedCount += handledCount;
        cursor.UpdatedAt      = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Processes one debit event. Returns <c>true</c> when the event is fully handled (success,
    /// idempotent no-op, terminal cancel, or an unrecoverable/poison payload we deliberately skip)
    /// and the cursor may advance; <c>false</c> only for UNEXPECTED transient errors, so the caller
    /// stops and retries from the current watermark next poll.
    /// </summary>
    private async Task<bool> ProcessEventAsync(
        LaundryGharDbContext db,
        IDispatcher          dispatcher,
        laundryghar.SharedDataModel.Entities.Kernel.OutboxEvent evt,
        CancellationToken    ct)
    {
        Guid partnerId, bookingId;
        decimal amount;

        try
        {
            using var doc = JsonDocument.Parse(evt.Payload);
            var root      = doc.RootElement;
            partnerId     = root.GetProperty("partnerId").GetGuid();
            bookingId     = root.GetProperty("bookingId").GetGuid();
            amount        = root.GetProperty("amount").GetDecimal();
        }
        catch (Exception ex)
        {
            // Poison message: malformed payload cannot be fixed by retrying. Skip so the consumer
            // does not wedge, but log loudly — a booking may be left uncharged.
            _logger.LogError(ex,
                "PartnerBookingDebitService: malformed payload for eventId={EventId}; skipping.", evt.Id);
            return true;
        }

        if (amount <= 0m)
        {
            _logger.LogWarning(
                "PartnerBookingDebitService: non-positive amount for bookingId={BookingId} (eventId={EventId}); skipping.",
                bookingId, evt.Id);
            return true;
        }

        try
        {
            // Idempotent: keyed on booking id inside the handler, so a redelivery is a no-op.
            await dispatcher.SendAsync(new DebitPartnerWalletCommand(
                PartnerId: partnerId,
                Amount:    amount,
                BookingId: bookingId,
                Notes:     $"Prepaid debit for partner booking {bookingId:N}",
                ActorId:   null), ct);

            _logger.LogInformation(
                "PartnerBookingDebitService: debited {Amount} from partnerId={PartnerId} for bookingId={BookingId}.",
                amount, partnerId, bookingId);
            return true;
        }
        catch (BusinessRuleException ex)
        {
            // Insufficient balance (race past the pre-check) or a frozen/closed wallet. Terminal —
            // the debit can never succeed, so cancel the unfunded booking and move on.
            _logger.LogWarning(ex,
                "PartnerBookingDebitService: debit rejected for bookingId={BookingId} (partnerId={PartnerId}); cancelling booking.",
                bookingId, partnerId);
            await CancelUnfundedBookingAsync(db, bookingId, partnerId, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unexpected/transient (DB connectivity, etc.) — do NOT advance; retry next poll.
            _logger.LogError(ex,
                "PartnerBookingDebitService: transient error debiting bookingId={BookingId}; will retry.",
                bookingId);
            return false;
        }
    }

    /// <summary>
    /// Cancels a booking whose prepaid debit could not be funded. Only a still-'requested' booking is
    /// cancelled — if it has already progressed (assigned/in_progress in a later wave) we leave it for
    /// manual/dispatch handling rather than clobber its state.
    /// </summary>
    private async Task CancelUnfundedBookingAsync(
        LaundryGharDbContext db,
        Guid                 bookingId,
        Guid                 partnerId,
        CancellationToken    ct)
    {
        var booking = await db.PartnerBookings
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.PartnerId == partnerId, ct);

        if (booking is null)
        {
            _logger.LogWarning(
                "PartnerBookingDebitService: bookingId={BookingId} not found to cancel.", bookingId);
            return;
        }

        if (booking.Status != "requested")
        {
            _logger.LogWarning(
                "PartnerBookingDebitService: bookingId={BookingId} is '{Status}', not 'requested'; " +
                "leaving as-is (needs manual review — debit failed but booking already progressed).",
                bookingId, booking.Status);
            return;
        }

        booking.Status    = "cancelled";
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PartnerBookingDebitService: cancelled unfunded bookingId={BookingId}.", bookingId);
    }
}
