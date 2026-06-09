using laundryghar.Logistics.Application.Payout;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
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
    Guid     Id,
    string   OrderNumber,
    string   LegType,        // "pickup" | "delivery" | "return"
    string   Status,         // assigned | started | arrived | completed | failed | cancelled
    bool     IsExpress,
    string   CustomerName,
    string?  CustomerPhone,  // E.164
    string   AddressLine,
    string?  ZoneLabel,
    decimal? DistanceKm,
    int?     EtaMinutes,
    string?  ScheduledTime,  // "HH:mm" (IST)
    int      GarmentCount,
    decimal  AmountDue,
    bool     IsPaid,
    bool     RequiresOtp,    // delivery/return legs that have an OTP on the order
    bool     OtpVerified,
    decimal  Payout,         // server-estimated rider earning for this leg (₹)
    double?  Lat,
    double?  Lng,
    short?   SequenceNumber,
    DateTimeOffset? CompletedAt,
    // ── Phase 2: drop-at-laundry round-trip ──────────────────────────────
    DateTimeOffset? CollectedAt,  // pickup: items collected from customer
    DateTimeOffset? DroppedAt,    // pickup: items dropped at the store/laundry
    string   Phase);              // to_customer|at_customer|to_store|dropped|completed|failed|cancelled|assigned

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
        "accepted"    => "assigned",   // employee riders skip an explicit accept step
        "rejected"    => "cancelled",
        "rescheduled" => "assigned",
        _             => s,            // assigned/started/arrived/completed/failed/cancelled pass through
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
        if (!string.IsNullOrWhiteSpace(c?.DisplayName))   return c!.DisplayName!;
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
            if (da.DroppedAt is not null)   return "dropped";      // dropped at the laundry
            if (da.CollectedAt is not null) return "to_store";     // collected, heading to the store
            if (st == "arrived")            return "at_customer";  // on site, awaiting collection
            return st == "started" ? "to_customer" : "assigned";
        }

        // delivery / return — single destination (the customer)
        if (st == "arrived") return "at_customer";
        return st == "started" ? "to_customer" : "assigned";
    }

    public static RiderTaskDto ToDto(DeliveryAssignment da, Order? o, Customer? c, CustomerAddress? addr, RiderPayoutSettings payout)
    {
        var isExpress    = o?.IsExpress ?? false;
        var amountDue    = o?.AmountDue ?? 0m;
        var isDelivery   = da.LegType is "delivery" or "return";
        // OTP applies to BOTH legs: the customer reads it out to confirm the handover —
        // collecting items at pickup AND receiving them at delivery.
        var legOtp       = isDelivery ? o?.DeliveryOtp : o?.PickupOtp;
        var requiresOtp  = !string.IsNullOrWhiteSpace(legOtp);

        // Scheduled time from the relevant order timestamp, rendered in IST.
        var scheduledAt = isDelivery ? o?.PromisedDeliveryAt : o?.PickupScheduledAt;
        var scheduled   = scheduledAt?.ToOffset(Ist).ToString("HH:mm");

        // Prefer the assignment's own geo, else the address geo.
        var pt  = da.GeoLocation ?? addr?.GeoLocation;
        double? lat = pt?.Y;
        double? lng = pt?.X;

        // Payout: the amount persisted at completion, else a live estimate from the
        // configured rates. COD bonus applies to a delivery that still has cash due.
        var hasCod = da.CodAmount is > 0m
                  || (isDelivery && amountDue > 0m && o?.PaymentStatus != "paid");
        var payoutAmt = da.PayoutAmount ?? payout.Compute(da.DistanceKm, isExpress, hasCod);

        return new RiderTaskDto(
            Id:            da.Id,
            OrderNumber:   o?.OrderNumber ?? "—",
            LegType:       da.LegType,
            Status:        MapStatus(da.Status),
            IsExpress:     isExpress,
            CustomerName:  CustomerName(c, addr),
            CustomerPhone: addr?.RecipientPhone ?? c?.PhoneE164,
            AddressLine:   BuildAddressLine(addr),
            ZoneLabel:     BuildZone(addr),
            DistanceKm:    da.DistanceKm,
            EtaMinutes:    da.DurationMinutes,
            ScheduledTime: scheduled,
            GarmentCount:  o?.TotalGarments ?? 0,
            AmountDue:     amountDue,
            IsPaid:        (o?.PaymentStatus == "paid") || amountDue <= 0m,
            RequiresOtp:   requiresOtp,
            OtpVerified:   da.OtpVerified,
            Payout:        payoutAmt,
            Lat:           lat,
            Lng:           lng,
            SequenceNumber: da.SequenceNumber,
            CompletedAt:   da.CompletedAt,
            CollectedAt:   da.CollectedAt,
            DroppedAt:     da.DroppedAt,
            Phase:         Phase(da));
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

        var startOfToday = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
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

        // Resolve the relevant address per leg (pickup→pickup_address, else delivery_address).
        var addrIds = rows
            .Where(x => x.o != null)
            .Select(x => x.da.LegType == "pickup" ? x.o!.PickupAddressId : x.o!.DeliveryAddressId)
            .Where(id => id.HasValue).Select(id => id!.Value)
            .Distinct().ToList();

        var addrById = addrIds.Count == 0
            ? new Dictionary<Guid, CustomerAddress>()
            : await _db.CustomerAddresses
                .Where(a => addrIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);

        CustomerAddress? AddrFor(DeliveryAssignment da, Order? o)
        {
            if (o is null) return null;
            var id = da.LegType == "pickup" ? o.PickupAddressId : o.DeliveryAddressId;
            return id.HasValue && addrById.TryGetValue(id.Value, out var a) ? a : null;
        }

        var payoutCfg = await PayoutConfig.LoadAsync(_db, q.BrandId, ct);
        var tasks = rows
            .Select(x => RiderTaskMapper.ToDto(x.da, x.o, x.c, AddrFor(x.da, x.o), payoutCfg))
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

public sealed record UpdateMyTaskStatusCommand(
    Guid AssignmentId, Guid UserId, Guid BrandId, string Status) : IRequest<RiderTaskResult>;

/// <summary>Result of a status/OTP mutation. Outcome distinguishes the 404/409 cases for the endpoint.</summary>
public sealed record RiderTaskResult(string Outcome, RiderTaskDto? Task = null, string? Error = null)
{
    public static RiderTaskResult NotFound()             => new("not_found");
    public static RiderTaskResult Conflict(string e)     => new("conflict", Error: e);
    public static RiderTaskResult Ok(RiderTaskDto t)     => new("ok", Task: t);
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
            da.UpdatedAt = now;
            da.UpdatedBy = cmd.UserId;
            await _db.SaveChangesAsync(ct);
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
            case "started":   da.StartedAt   ??= now; break;
            case "arrived":   da.ArrivedAt   ??= now; break;
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
        await _db.SaveChangesAsync(ct);

        return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr, payoutCfg));
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
        var expected   = isDelivery ? o?.DeliveryOtp : o?.PickupOtp;

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
