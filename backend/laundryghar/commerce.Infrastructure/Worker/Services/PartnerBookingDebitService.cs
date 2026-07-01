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
/// Design — NO-SKIP delivery via a per-event inbox marker (this is a MONEY path: a skipped debit is
/// a free booking, i.e. direct revenue loss, and the idempotent command cannot recover an event that
/// was never delivered):
///   - Per-event processed marker (<c>OutboxConsumedEvent</c>, kernel.outbox_consumed_events,
///     consumer_name = 'partner_booking_debit'). We query the set of UNPROCESSED debit events with an
///     anti-join (an event is eligible iff it has no marker for this consumer) — NOT by a moving
///     <c>OccurredAt</c> watermark. Eligibility is order-independent, so an event can never be stepped
///     over. This structurally fixes the two watermark defects: (1) an earlier-<c>OccurredAt</c> row
///     that COMMITS after a later one the watermark already passed, and (2) a burst of &gt;BatchSize
///     rows sharing one <c>OccurredAt</c> where a strict '&gt;' cursor skips the tied remainder.
///     (<see cref="LoyaltyEarnService"/> still uses the old time-watermark cursor and shares that
///     latent skip risk; the fix is deliberately scoped to this money path — see the class remarks in
///     that file. Migrating loyalty to this same inbox table is a safe follow-up.)
///   - We do NOT mutate <c>OutboxEvent.Status</c>/<c>PublishedAt</c> — those are exclusively owned by
///     <see cref="OutboxEventRelayService"/> (the generic broker relay also drains these rows; the two
///     consumers are independent and never race, because they touch disjoint state).
///   - Idempotency: <see cref="DebitPartnerWalletCommand"/> keys the ledger row on the booking id
///     (unique on partner_wallet_transactions.idempotency_key), so a redelivered event is a no-op.
///     This is the AUTHORITATIVE double-debit guard — the booking-time pre-check only narrows the race.
///     Because re-delivery is safe, we favour a mechanism that never skips even if it occasionally
///     re-delivers: the marker is written only AFTER the debit commits (or the booking is terminally
///     cancelled), so a crash before the marker just re-delivers the (idempotent) debit next poll.
///   - Failure path: an insufficient-balance <see cref="BusinessRuleException"/> (a race that slipped
///     past the pre-check, or a frozen wallet) is terminal — retrying cannot succeed — so we cancel the
///     unfunded booking (Status → 'cancelled') and mark the event processed. Only UNEXPECTED (transient)
///     errors leave the event UNMARKED so the next poll retries it.
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

        // NO-SKIP delivery: query the UNPROCESSED debit events via an anti-join against this
        // consumer's inbox (kernel.outbox_consumed_events). An event is eligible iff it has NO
        // marker for this consumer — eligibility is "not yet processed", NOT "occurred after a
        // moving time-watermark". Because it is order-independent, an event can never be stepped
        // over, whether it commits out of OccurredAt order or shares its OccurredAt with a whole
        // batch of siblings. OrderBy(OccurredAt) is fairness (oldest first) only, not correctness.
        var events = await db.OutboxEvents
            .Where(e => e.EventType == EventType
                     && !db.OutboxConsumedEvents.Any(
                            c => c.ConsumerName == ConsumerName && c.EventId == e.Id))
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (events.Count == 0) return;

        foreach (var evt in events)
        {
            var handled = await ProcessEventAsync(db, dispatcher, evt, ct);
            if (!handled)
                break; // transient failure — leave this (and later) events UNMARKED; next poll retries.

            // Mark processed ONLY after the debit is durable (or the booking terminally cancelled),
            // so a crash before this point re-delivers the idempotent debit — never a skip.
            await MarkProcessedAsync(db, evt.Id, ct);
        }
    }

    /// <summary>
    /// Durably records that this consumer has finished with <paramref name="eventId"/> so it is never
    /// re-queried. Committed in its own SaveChanges after the debit's transaction, so the two never
    /// share a transaction boundary. A duplicate-key collision (defensive — this consumer is
    /// single-instance) means a concurrent pass already marked it: detach and treat as done, since the
    /// debit is idempotent regardless.
    /// </summary>
    private async Task MarkProcessedAsync(LaundryGharDbContext db, Guid eventId, CancellationToken ct)
    {
        var marker = new laundryghar.SharedDataModel.Entities.Kernel.OutboxConsumedEvent
        {
            ConsumerName = ConsumerName,
            EventId      = eventId,
            ProcessedAt  = DateTimeOffset.UtcNow
        };

        db.OutboxConsumedEvents.Add(marker);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(marker).State = EntityState.Detached;
            _logger.LogDebug(
                "PartnerBookingDebitService: eventId={EventId} already marked processed; ignoring.", eventId);
        }
    }

    /// <summary>
    /// Processes one debit event. Returns <c>true</c> when the event is fully handled (success,
    /// idempotent no-op, terminal cancel, or an unrecoverable/poison payload we deliberately skip)
    /// and the caller may mark it processed; <c>false</c> only for UNEXPECTED transient errors, so the
    /// caller leaves it UNMARKED and the next poll retries it.
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
