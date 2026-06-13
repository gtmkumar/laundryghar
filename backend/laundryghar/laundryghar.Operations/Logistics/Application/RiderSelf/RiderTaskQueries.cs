using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using System.Text.Json;
using laundryghar.Logistics.Application.Payout;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.Orders.Application.Common;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Enums;
using laundryghar.SharedDataModel.Logistics;
using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ─────────────────────────────────────────────────────────────────────────────
// Rider-facing per-order task feed.
//
// A "task" is one leg (pickup / delivery / return) of an order, modelled by
// order_lifecycle.delivery_assignments. The admin/dispatch side creates these;
// this is the rider's read + act surface. See HANDOFF backlog "rider-tasks API".
//
// Security note: the delivery OTP is NEVER returned to the device. The customer
// reads it out, the rider types it, and the server verifies (VerifyTaskOtp).
// Exposing `RequiresOtp` only is what lets the client show/hide the OTP field.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Mobile-facing view of one pickup/delivery/return leg.</summary>
public sealed record RiderTaskDto(
    Guid Id,
    string OrderNumber,
    string LegType,        // "pickup" | "delivery" | "return"
    string Status,         // assigned | started | arrived | completed | failed | cancelled
    bool IsExpress,
    string CustomerName,
    string? CustomerPhone,  // E.164
    string AddressLine,
    string? ZoneLabel,
    decimal? DistanceKm,
    int? EtaMinutes,
    string? ScheduledTime,  // "HH:mm" (IST)
    int GarmentCount,
    decimal AmountDue,
    bool IsPaid,
    bool RequiresOtp,    // delivery/return legs that have an OTP on the order
    bool OtpVerified,
    decimal Payout,         // server-estimated rider earning for this leg (₹)
    double? Lat,
    double? Lng,
    short? SequenceNumber,
    DateTimeOffset? CompletedAt,
    // ── Phase 2: drop-at-laundry round-trip ──────────────────────────────
    DateTimeOffset? CollectedAt,  // pickup: items collected from customer
    DateTimeOffset? DroppedAt,    // pickup: items dropped at the store/laundry
    string Phase);              // to_customer|at_customer|to_store|dropped|completed|failed|cancelled|assigned

// ── Shared mapping / helpers ───────────────────────────────────────────────────

internal static class RiderTaskMapper
{
    /// <summary>Non-terminal statuses that are always shown in the active feed.</summary>
    internal static readonly string[] OpenStatuses =
        ["assigned", "accepted", "started", "arrived"];

    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    /// <summary>Maps backend DeliveryAssignmentStatus → the slimmer status the app understands.</summary>
    private static string MapStatus(string s) => s switch
    {
        "accepted" => "assigned",   // employee riders skip an explicit accept step
        "rejected" => "cancelled",
        "rescheduled" => "assigned",
        _ => s,            // assigned/started/arrived/completed/failed/cancelled pass through
    };

    private static string BuildAddressLine(CustomerAddress? a)
    {
        if (a is null) return "Address on file";
        var parts = new[] { a.FlatNumber, a.BuildingName, a.AddressLine1, a.Area }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var line = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(line) ? a.City : line;
    }

    private static string? BuildZone(CustomerAddress? a)
    {
        if (a is null) return null;
        var z = a.Area ?? a.City;
        return string.IsNullOrWhiteSpace(z) ? null : z;
    }

    private static string CustomerName(Customer? c, CustomerAddress? a)
    {
        if (!string.IsNullOrWhiteSpace(a?.RecipientName)) return a!.RecipientName!;
        if (!string.IsNullOrWhiteSpace(c?.DisplayName)) return c!.DisplayName!;
        var full = $"{c?.FirstName} {c?.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) return full;
        return "Customer";
    }

    /// <summary>
    /// Fine-grained step within a leg, derived from the lifecycle timestamps. For a
    /// pickup this captures the two-part round-trip (to_customer → at_customer →
    /// to_store → dropped); delivery/return have no store drop.
    /// </summary>
    private static string Phase(DeliveryAssignment da)
    {
        var st = MapStatus(da.Status);
        if (st is "completed" or "failed" or "cancelled") return st;

        if (da.LegType == "pickup")
        {
            if (da.DroppedAt is not null) return "dropped";      // dropped at the laundry
            if (da.CollectedAt is not null) return "to_store";     // collected, heading to the store
            if (st == "arrived") return "at_customer";  // on site, awaiting collection
            return st == "started" ? "to_customer" : "assigned";
        }

        // delivery / return — single destination (the customer)
        if (st == "arrived") return "at_customer";
        return st == "started" ? "to_customer" : "assigned";
    }

    /// <param name="pr">
    /// DEFECT 4 — the source pickup request for a pickup leg. Pickup-leg assignments
    /// link to a PickupRequest (not an Order: order_id is null), so without this the
    /// order-derived fields all fell back to placeholders ("—", "Customer",
    /// "Address on file", 0, 0). When <paramref name="o"/> is null we read the
    /// customer-facing fields from <paramref name="pr"/> instead.
    /// </param>
    public static RiderTaskDto ToDto(
        DeliveryAssignment da, Order? o, Customer? c, CustomerAddress? addr,
        RiderPayoutSettings payout, PickupRequest? pr = null)
    {
        var isDelivery = da.LegType is "delivery" or "return";

        var isExpress = o?.IsExpress ?? pr?.IsExpress ?? false;
        // Amount due: the order balance for deliveries; the pickup request's estimated
        // amount for an un-converted pickup leg (what the rider collects as COD).
        var amountDue = o?.AmountDue ?? (o is null ? pr?.EstimatedAmount ?? 0m : 0m);

        // OTP applies to BOTH legs: the customer reads it out to confirm the handover —
        // collecting items at pickup AND receiving them at delivery.
        var legOtp = isDelivery ? o?.DeliveryOtp : o?.PickupOtp;
        var requiresOtp = !string.IsNullOrWhiteSpace(legOtp);

        // Scheduled time: order timestamp when available; else the pickup request's
        // scheduled window start (rendered in IST as "HH:mm").
        string? scheduled;
        var scheduledAt = isDelivery ? o?.PromisedDeliveryAt : o?.PickupScheduledAt;
        if (scheduledAt is not null)
            scheduled = scheduledAt.Value.ToOffset(Ist).ToString("HH:mm");
        else if (o is null && pr is not null)
            scheduled = pr.PickupWindowStart.ToString("HH:mm");
        else
            scheduled = null;

        // Prefer the assignment's own geo, else the address geo.
        var pt = da.GeoLocation ?? addr?.GeoLocation;
        double? lat = pt?.Y;
        double? lng = pt?.X;

        // Payout: the amount persisted at completion, else a live estimate from the
        // configured rates. COD bonus applies to a delivery that still has cash due.
        var hasCod = da.CodAmount is > 0m
                  || (isDelivery && amountDue > 0m && o?.PaymentStatus != "paid");
        var payoutAmt = da.PayoutAmount ?? payout.Compute(da.DistanceKm, isExpress, hasCod);

        var garmentCount = o?.TotalGarments ?? (o is null ? pr?.EstimatedItems ?? 0 : 0);
        var isPaid = (o?.PaymentStatus == "paid") || amountDue <= 0m;

        return new RiderTaskDto(
            Id: da.Id,
            OrderNumber: o?.OrderNumber ?? pr?.RequestNumber ?? "—",
            LegType: da.LegType,
            Status: MapStatus(da.Status),
            IsExpress: isExpress,
            CustomerName: CustomerName(c, addr),
            CustomerPhone: addr?.RecipientPhone ?? c?.PhoneE164,
            AddressLine: BuildAddressLine(addr),
            ZoneLabel: BuildZone(addr),
            DistanceKm: da.DistanceKm,
            EtaMinutes: da.DurationMinutes,
            ScheduledTime: scheduled,
            GarmentCount: garmentCount,
            AmountDue: amountDue,
            IsPaid: isPaid,
            RequiresOtp: requiresOtp,
            OtpVerified: da.OtpVerified,
            Payout: payoutAmt,
            Lat: lat,
            Lng: lng,
            SequenceNumber: da.SequenceNumber,
            CompletedAt: da.CompletedAt,
            CollectedAt: da.CollectedAt,
            DroppedAt: da.DroppedAt,
            Phase: Phase(da));
    }
}

// ── Get my tasks for today ─────────────────────────────────────────────────────

public sealed record GetMyTasksTodayQuery(Guid UserId, Guid BrandId) : IRequest<List<RiderTaskDto>>;

public sealed class GetMyTasksTodayHandler : IRequestHandler<GetMyTasksTodayQuery, List<RiderTaskDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetMyTasksTodayHandler(LaundryGharDbContext db) => _db = db;

    public async Task<List<RiderTaskDto>> Handle(GetMyTasksTodayQuery q, CancellationToken ct)
    {
        var rider = await _db.Riders
            .Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return [];

        // DEFECT 7: "today" must mean the rider's local (IST/store-tz) calendar day,
        // not the UTC day. At 04:30 IST the UTC day is still yesterday, which dropped
        // completed legs from the feed. Bracket "today" by the local-day UTC bounds.
        var tz = LocalDateRange.Resolve(LocalDateRange.DefaultTimeZoneId);
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
        var startOfToday = LocalDateRange.StartUtc(localToday, tz);
        var open = RiderTaskMapper.OpenStatuses;

        // delivery_assignments (this rider) ⟕ orders ⟕ customers
        var rows = await (
            from da in _db.DeliveryAssignments
            where da.RiderId == rider.Id && da.BrandId == q.BrandId
               && (open.Contains(da.Status)
                   || ((da.Status == "completed" || da.Status == "failed")
                       && da.CompletedAt >= startOfToday))
            join o in _db.Orders
                on new { I = da.OrderId, C = da.OrderCreatedAt }
                equals new { I = (Guid?)o.Id, C = (DateTimeOffset?)o.CreatedAt }
                into oj
            from o in oj.DefaultIfEmpty()
            join c in _db.Customers on o.CustomerId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            select new { da, o, c })
            .ToListAsync(ct);

        // DEFECT 4: pickup-leg assignments carry pickup_request_id (order_id is null),
        // so the order join above misses and every field fell back to a placeholder.
        // Load the linked pickup requests and resolve customer/address from them.
        var pickupReqIds = rows
            .Where(x => x.o == null && x.da.PickupRequestId.HasValue)
            .Select(x => x.da.PickupRequestId!.Value)
            .Distinct().ToList();

        var prById = pickupReqIds.Count == 0
            ? new Dictionary<Guid, PickupRequest>()
            : await _db.PickupRequests
                .Where(p => pickupReqIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        PickupRequest? PrFor(DeliveryAssignment da) =>
            da.PickupRequestId.HasValue && prById.TryGetValue(da.PickupRequestId.Value, out var p) ? p : null;

        // Resolve the relevant address per leg — order legs use the order's pickup/
        // delivery address; pickup-request legs use the request's address.
        var addrIds = rows
            .Where(x => x.o != null)
            .Select(x => x.da.LegType == "pickup" ? x.o!.PickupAddressId : x.o!.DeliveryAddressId)
            .Where(id => id.HasValue).Select(id => id!.Value)
            .Concat(prById.Values.Select(p => p.AddressId))
            .Distinct().ToList();

        var addrById = addrIds.Count == 0
            ? new Dictionary<Guid, CustomerAddress>()
            : await _db.CustomerAddresses
                .Where(a => addrIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);

        // Customers for pickup-request legs (order-leg customers came from the join).
        var prCustomerIds = prById.Values.Select(p => p.CustomerId).Distinct().ToList();
        var custById = prCustomerIds.Count == 0
            ? new Dictionary<Guid, Customer>()
            : await _db.Customers
                .Where(c => prCustomerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

        CustomerAddress? AddrFor(DeliveryAssignment da, Order? o, PickupRequest? pr)
        {
            Guid? id = o is not null
                ? (da.LegType == "pickup" ? o.PickupAddressId : o.DeliveryAddressId)
                : pr?.AddressId;
            return id.HasValue && addrById.TryGetValue(id.Value, out var a) ? a : null;
        }

        Customer? CustFor(Customer? joined, PickupRequest? pr)
        {
            if (joined is not null) return joined;
            return pr is not null && custById.TryGetValue(pr.CustomerId, out var c) ? c : null;
        }

        var payoutCfg = await PayoutConfig.LoadAsync(_db, q.BrandId, ct);
        var tasks = rows
            .Select(x =>
            {
                var pr = x.o == null ? PrFor(x.da) : null;
                return RiderTaskMapper.ToDto(
                    x.da, x.o, CustFor(x.c, pr), AddrFor(x.da, x.o, pr), payoutCfg, pr);
            })
            .ToList();

        // Active work first, by route sequence then scheduled time; completed sink to the bottom.
        return tasks
            .OrderBy(t => t.Status is "completed" or "failed" or "cancelled" ? 1 : 0)
            .ThenBy(t => t.SequenceNumber ?? short.MaxValue)
            .ThenBy(t => t.ScheduledTime ?? "99:99")
            .ToList();
    }
}

// ── Update my task status (start / arrive / complete / fail) ───────────────────

/// <param name="FailureReason">Reason code persisted on the assignment when Status='failed' (nullable).</param>
/// <param name="FailureNote">Optional free-text note for a failed status (nullable).</param>
public sealed record UpdateMyTaskStatusCommand(
    Guid AssignmentId, Guid UserId, Guid BrandId, string Status,
    string? FailureReason = null, string? FailureNote = null) : IRequest<RiderTaskResult>;

/// <summary>Result of a status/OTP mutation. Outcome distinguishes the 404/409 cases for the endpoint.</summary>
public sealed record RiderTaskResult(string Outcome, RiderTaskDto? Task = null, string? Error = null)
{
    public static RiderTaskResult NotFound() => new("not_found");
    public static RiderTaskResult Conflict(string e) => new("conflict", Error: e);
    public static RiderTaskResult Ok(RiderTaskDto t) => new("ok", Task: t);
}

public sealed class UpdateMyTaskStatusHandler : IRequestHandler<UpdateMyTaskStatusCommand, RiderTaskResult>
{
    private readonly LaundryGharDbContext _db;
    public UpdateMyTaskStatusHandler(LaundryGharDbContext db) => _db = db;

    private static readonly string[] Allowed =
        ["started", "arrived", "collected", "completed", "failed"];

    public async Task<RiderTaskResult> Handle(UpdateMyTaskStatusCommand cmd, CancellationToken ct)
    {
        if (!Allowed.Contains(cmd.Status))
            return RiderTaskResult.Conflict($"Unsupported status '{cmd.Status}'.");

        var rider = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return RiderTaskResult.NotFound();

        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(x => x.Id == cmd.AssignmentId
                                   && x.RiderId == rider.Id
                                   && x.BrandId == cmd.BrandId, ct);
        if (da is null) return RiderTaskResult.NotFound();

        var (o, c, addr) = await LoadOrderAsync(da, ct);
        var now = DateTimeOffset.UtcNow;
        var payoutCfg = await PayoutConfig.LoadAsync(_db, cmd.BrandId, ct);

        // "collected" is a pickup sub-step, not a DB status (the status CHECK has no
        // such value). Record collected_at and keep the leg 'arrived' (collection
        // happens on-site at the customer); the rider then drives to the store.
        if (cmd.Status == "collected")
        {
            if (da.LegType != "pickup")
                return RiderTaskResult.Conflict("Only pickup legs can be collected.");
            da.CollectedAt ??= now;
            if (da.Status == "started") { da.Status = "arrived"; da.ArrivedAt ??= now; }

            // COD-CASH (pickup leg): collecting the items IS collecting the cash. Stamp
            // cod_collected_at and ensure cod_amount is set so this collection enters the
            // SAME rider-cash settlement pipeline the delivery legs use (the outstanding/
            // settle queries key off cod_amount != null && settlement_id == null, leg-type
            // agnostic). Mirrors the delivery-completion COD block below — no new mechanism.
            // Only when cash is actually due (PaymentPreference == "cod" && amount > 0);
            // wallet/upi-deferred pickups collect nothing. Idempotent on re-tap via ??=.
            var cod = da.CodAmount ?? await ResolvePickupCodAsync(da, ct);
            if (cod is > 0m)
            {
                da.CodAmount ??= cod;
                da.CodCollectedAt ??= now;
            }

            da.UpdatedAt = now;
            da.UpdatedBy = cmd.UserId;

            // DEFECT 6: collection reflects "picked_up" in the order lifecycle. Advance
            // the linked order (if any) up to picked_up through the legal path, in the
            // same transaction as the assignment change.
            await AdvancePickupLegAsync(da, pickupTarget: null, orderTarget: OrderStatus.PickedUp,
                                        now: now, userId: cmd.UserId, ct: ct);
            return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr, payoutCfg));
        }

        // Any leg with an OTP (pickup or delivery) must be verified before completing.
        var legOtp = da.LegType == "pickup" ? o?.PickupOtp : o?.DeliveryOtp;
        if (cmd.Status == "completed"
            && !string.IsNullOrWhiteSpace(legOtp)
            && !da.OtpVerified)
            return RiderTaskResult.Conflict("OTP must be verified before completing.");

        da.Status = cmd.Status;
        switch (cmd.Status)
        {
            case "started": da.StartedAt ??= now; break;
            case "arrived": da.ArrivedAt ??= now; break;
            case "failed":
                // Persist the structured reason code and free-text note provided by the rider.
                // CancellationReason re-used for failure reason; Notes appended idempotently.
                // Allowed reason codes: customer_unavailable | address_issue | customer_refused | other.
                if (!string.IsNullOrWhiteSpace(cmd.FailureReason))
                    da.CancellationReason ??= cmd.FailureReason.Trim();
                if (!string.IsNullOrWhiteSpace(cmd.FailureNote))
                    da.Notes ??= cmd.FailureNote.Trim();
                break;
            case "completed":
                da.CompletedAt ??= now;
                // Completing a pickup IS the drop-at-laundry confirmation — stamp the
                // drop if the store geofence hadn't already caught it.
                if (da.LegType == "pickup")
                {
                    da.DroppedAt ??= now;
                }
                else
                {
                    // Delivery/return — if the order still has a balance due, the rider
                    // collected it as COD cash. Record it for settlement (Phase 3).
                    var due = o?.AmountDue ?? 0m;
                    if (da.CodAmount is null && o?.PaymentStatus != "paid" && due > 0m)
                    {
                        da.CodAmount = due;
                        da.CodCollectedAt = now;
                    }
                }
                // Phase 4: persist the leg payout from the configured rates.
                da.PayoutAmount ??= payoutCfg.Compute(da.DistanceKm, o?.IsExpress ?? false, da.CodAmount is > 0m);
                break;
        }
        da.UpdatedAt = now;
        da.UpdatedBy = cmd.UserId;

        // ── DEFECT 6: pickup-leg completion advances the pickup request + order ────
        // A completed pickup leg means the items were collected AND dropped at the
        // store. Advance the pickup request assigned→completed and the linked order
        // up to 'received' (dropped at store) through the legal happy-path. Done in
        // its own transaction (mirrors the delivery side-effect block below).
        if (cmd.Status == "completed" && da.LegType == "pickup")
        {
            await AdvancePickupLegAsync(da, pickupTarget: "completed", orderTarget: OrderStatus.Received,
                                        now: now, userId: cmd.UserId, ct: ct);
        }

        // ── DOC-1: Transactional delivery-completion side-effects ─────────────────
        // When a delivery/return leg completes, atomically:
        //   1. Transition the order out_for_delivery → delivered
        //   2. Append an order_status_history row
        //   3. Create a COD payment row (if cash was collected)
        //   4. Emit a delivery.completed outbox event
        // All mutations share one SaveChanges inside a retry-capable transaction.
        // The RiderLoadHelper.DecrementAsync call is deliberately kept outside the
        // transaction because it issues its own SaveChanges and does not need to
        // roll back if the outer tx succeeds.
        var isDeliveryCompletion = cmd.Status == "completed"
                                && da.LegType is "delivery" or "return"
                                && o is not null;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // H1 idempotency: the ENTIRE side-effect block (history row, COD payment,
            // outbox event) is gated on first-completion only (DeliveredAt == null).
            // A rider double-tap on an already-delivered order is a no-op — the
            // assignment status update above is still saved (da.CompletedAt ??= now),
            // but none of the order-level side effects fire again.
            if (isDeliveryCompletion && o!.DeliveredAt == null)
            {
                // 1. Order status transition
                o.Status = "delivered";
                o.DeliveredAt = now;
                o.Version += 1;
                o.UpdatedAt = now;
                o.UpdatedBy = cmd.UserId;

                // 2. Order status history
                _db.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = o.Id,
                    OrderCreatedAt = o.CreatedAt,
                    BrandId = da.BrandId,
                    FromStatus = "out_for_delivery",
                    ToStatus = "delivered",
                    ChangedAt = now,
                    ChangedByType = "system",
                    ChangedById = cmd.UserId,
                    Reason = "Delivery completed by rider",
                    CustomerNotified = false,
                    Metadata = "{}",
                    CreatedAt = now,
                    CreatedBy = cmd.UserId,
                });

                // 3. COD payment row — only when cash was collected AND no completed
                //    payment already exists for this assignment (re-call guard).
                if (da.CodAmount is > 0m)
                {
                    var codAlreadyRecorded = await _db.Payments
                        .AnyAsync(p => p.Gateway == "cod"
                                    && p.OrderId == o.Id
                                    && p.BrandId == da.BrandId
                                    && p.Status == CommercePaymentStatus.Succeeded, ct);

                    if (!codAlreadyRecorded)
                    {
                        var cod = da.CodAmount!.Value;
                        _db.Payments.Add(new Payment
                        {
                            Id = Guid.NewGuid(),
                            BrandId = da.BrandId,
                            CustomerId = o.CustomerId,
                            OrderId = o.Id,
                            OrderCreatedAt = o.CreatedAt,
                            // DEF-A1: must use constraint-valid values from PaymentPurpose / CommercePaymentStatus.
                            // "order_payment" violates the CHECK; valid value is "order".
                            // "completed"     violates the CHECK; valid value is "succeeded".
                            PaymentPurpose = PaymentPurpose.Order,
                            PaymentNumber = $"COD-{o.OrderNumber}-{now:yyyyMMddHHmmss}",
                            Amount = cod,
                            ConvenienceFee = 0m,
                            GatewayCharge = 0m,
                            NetAmount = cod,
                            CurrencyCode = o.CurrencyCode,
                            Direction = 1,
                            Gateway = "cod",
                            Status = CommercePaymentStatus.Succeeded,
                            InitiatedAt = now,
                            CompletedAt = now,
                            Metadata = "{}",
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = cmd.UserId,
                            UpdatedBy = cmd.UserId,
                        });

                        o.AmountPaid += cod;
                        if (o.AmountPaid >= o.GrandTotal)
                            o.PaymentStatus = "paid";
                    }
                }

                // 4. Outbox event
                _db.OutboxEvents.Add(new OutboxEvent
                {
                    Id = Guid.NewGuid(),
                    BrandId = da.BrandId,
                    AggregateType = "order",
                    AggregateId = o.Id,
                    EventType = "delivery.completed",
                    EventVersion = 1,
                    Payload = JsonSerializer.Serialize(new
                    {
                        orderId = o.Id,
                        orderNumber = o.OrderNumber,
                        brandId = da.BrandId,
                        riderId = da.RiderId,
                        assignmentId = da.Id,
                        legType = da.LegType,
                        codCollected = da.CodAmount ?? 0m,
                        deliveredAt = now,
                    }),
                    Metadata = "{}",
                    OccurredAt = now,
                    Status = "pending",
                    CreatedAt = now,
                    CreatedBy = cmd.UserId,
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        // Decrement current_load when this leg reaches a terminal state.
        if (cmd.Status is "completed" or "failed")
            await RiderLoadHelper.DecrementAsync(_db, da.RiderId, ct);

        return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr, payoutCfg));
    }

    /// <summary>
    /// COD-CASH (pickup leg): resolves the cash due for a pickup leg from its linked
    /// pickup request, applying the SAME rule as assign-time
    /// (<see cref="laundryghar.Orders.Application.Pickup.Commands.AssignPickupHandler.ResolvePickupCodAmount"/>):
    /// cash is due only when PaymentPreference == "cod" and EstimatedAmount &gt; 0.
    /// Returns null for non-pickup legs, unlinked legs, or non-COD preferences. Used as a
    /// fallback on collect when the assignment was created before COD seeding (assign-time
    /// normally sets it).
    /// </summary>
    private async Task<decimal?> ResolvePickupCodAsync(DeliveryAssignment da, CancellationToken ct)
    {
        if (da.LegType != "pickup" || da.PickupRequestId is null) return null;
        var pr = await _db.PickupRequests
            .Where(p => p.Id == da.PickupRequestId.Value)
            .Select(p => new { p.PaymentPreference, p.EstimatedAmount })
            .FirstOrDefaultAsync(ct);
        if (pr is null) return null;
        return laundryghar.Orders.Application.Pickup.Commands.AssignPickupHandler
            .ResolvePickupCodAmount(pr.PaymentPreference, pr.EstimatedAmount);
    }

    private async Task<(Order?, Customer?, CustomerAddress?)> LoadOrderAsync(DeliveryAssignment da, CancellationToken ct)
    {
        if (da.OrderId is null || da.OrderCreatedAt is null) return (null, null, null);

        var o = await _db.Orders.FirstOrDefaultAsync(
            x => x.Id == da.OrderId && x.CreatedAt == da.OrderCreatedAt, ct);
        if (o is null) return (null, null, null);

        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == o.CustomerId, ct);
        var addrId = da.LegType == "pickup" ? o.PickupAddressId : o.DeliveryAddressId;
        var addr = addrId.HasValue
            ? await _db.CustomerAddresses.FirstOrDefaultAsync(a => a.Id == addrId.Value, ct)
            : null;
        return (o, c, addr);
    }

    /// <summary>
    /// DEFECT 6 — advances a pickup leg's pickup_request and (when linked) its order to
    /// reflect physical progress: collection → order 'picked_up'; drop-at-store →
    /// pickup_request 'completed' + order 'received'. Resolution path:
    ///   delivery_assignment.pickup_request_id → pickup_request.converted_order_id → order
    /// (an order linked directly via assignment.order_id is also honoured). When no
    /// order is linked (the QA flow where the booking was never converted) only the
    /// pickup request advances — no illegal jump is forced. Each order hop along the
    /// legal happy-path writes an order_status_history row. Idempotent: a status
    /// already at/beyond the target is a no-op. Runs in a retry-capable transaction.
    /// </summary>
    private async Task AdvancePickupLegAsync(
        DeliveryAssignment da, string? pickupTarget, string orderTarget,
        DateTimeOffset now, Guid userId, CancellationToken ct)
    {
        // Resolve the pickup request (the canonical link for a pickup leg).
        PickupRequest? pr = da.PickupRequestId.HasValue
            ? await _db.PickupRequests.FirstOrDefaultAsync(p => p.Id == da.PickupRequestId.Value, ct)
            : null;

        // Resolve the linked order: prefer the assignment's own order link, else the
        // pickup request's converted order.
        Order? order = null;
        if (da.OrderId is not null && da.OrderCreatedAt is not null)
        {
            order = await _db.Orders.FirstOrDefaultAsync(
                x => x.Id == da.OrderId && x.CreatedAt == da.OrderCreatedAt, ct);
        }
        else if (pr?.ConvertedOrderId is not null)
        {
            order = await _db.Orders.FirstOrDefaultAsync(x => x.Id == pr.ConvertedOrderId.Value, ct);
        }

        // Nothing to advance and nothing to persist beyond the caller's own changes.
        if (pr is null && order is null) return;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // 1. Advance the pickup request status (idempotent; only forward).
            if (pr is not null && pickupTarget is not null
                && !string.Equals(pr.Status, pickupTarget, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pr.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pr.Status, "converted", StringComparison.OrdinalIgnoreCase))
            {
                pr.Status = pickupTarget;
                pr.UpdatedAt = now;
                pr.UpdatedBy = userId;
            }

            // 2. Walk the order forward along legal single-step transitions, writing a
            //    history row per hop. Off-path / already-advanced orders → no-op.
            if (order is not null)
            {
                var hops = OrderStateMachine.ForwardPath(order.Status, orderTarget);
                foreach (var next in hops)
                {
                    var from = order.Status;
                    order.Status = next;
                    order.Version += 1;
                    order.UpdatedAt = now;
                    order.UpdatedBy = userId;

                    if (next == OrderStatus.PickedUp) order.PickedUpAt ??= now;
                    if (next == OrderStatus.Received) order.ReceivedAt ??= now;

                    _db.OrderStatusHistories.Add(new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        OrderCreatedAt = order.CreatedAt,
                        BrandId = da.BrandId,
                        FromStatus = from,
                        ToStatus = next,
                        ChangedAt = now,
                        ChangedByType = "system",
                        ChangedById = userId,
                        Reason = "Pickup leg progressed by rider",
                        CustomerNotified = false,
                        Metadata = "{}",
                        CreatedAt = now,
                        CreatedBy = userId,
                    });
                }
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}

// ── Verify delivery OTP (server-side; OTP never leaves the server) ─────────────

public sealed record VerifyTaskOtpCommand(
    Guid AssignmentId, Guid UserId, Guid BrandId, string Code) : IRequest<RiderTaskResult>;

public sealed class VerifyTaskOtpHandler : IRequestHandler<VerifyTaskOtpCommand, RiderTaskResult>
{
    private readonly LaundryGharDbContext _db;
    public VerifyTaskOtpHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderTaskResult> Handle(VerifyTaskOtpCommand cmd, CancellationToken ct)
    {
        var rider = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => new { r.Id }).FirstOrDefaultAsync(ct);
        if (rider is null) return RiderTaskResult.NotFound();

        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(x => x.Id == cmd.AssignmentId
                                   && x.RiderId == rider.Id
                                   && x.BrandId == cmd.BrandId, ct);
        if (da is null) return RiderTaskResult.NotFound();

        var o = (da.OrderId is not null && da.OrderCreatedAt is not null)
            ? await _db.Orders.FirstOrDefaultAsync(
                x => x.Id == da.OrderId && x.CreatedAt == da.OrderCreatedAt, ct)
            : null;

        var isDelivery = da.LegType is "delivery" or "return";
        var expected = isDelivery ? o?.DeliveryOtp : o?.PickupOtp;

        var now = DateTimeOffset.UtcNow;
        da.OtpAttemptedAt = now;
        da.UpdatedAt = now;

        var supplied = (cmd.Code ?? string.Empty).Trim();
        var ok = !string.IsNullOrWhiteSpace(expected)
                 && string.Equals(expected.Trim(), supplied, StringComparison.Ordinal);

        if (ok)
        {
            da.OtpVerified = true;
            // A verified pickup OTP IS the collection handshake — stamp collected_at
            // so the geofence can begin watching for the drop at the store.
            if (da.LegType == "pickup") da.CollectedAt ??= now;
        }
        await _db.SaveChangesAsync(ct);

        if (!ok) return RiderTaskResult.Conflict("Incorrect OTP.");

        var c = o is not null ? await _db.Customers.FirstOrDefaultAsync(x => x.Id == o.CustomerId, ct) : null;
        var addrId = o is null ? (Guid?)null : (da.LegType == "pickup" ? o.PickupAddressId : o.DeliveryAddressId);
        var addr = addrId.HasValue
            ? await _db.CustomerAddresses.FirstOrDefaultAsync(a => a.Id == addrId.Value, ct)
            : null;

        var payoutCfg = await PayoutConfig.LoadAsync(_db, cmd.BrandId, ct);
        return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr, payoutCfg));
    }
}
