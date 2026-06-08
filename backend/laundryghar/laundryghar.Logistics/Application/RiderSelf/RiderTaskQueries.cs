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
    DateTimeOffset? CompletedAt);

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

    /// <summary>Transparent estimate until a real per-leg payout model exists.</summary>
    private static decimal EstimatePayout(decimal? distanceKm, bool isExpress)
    {
        var d = distanceKm ?? 2m;
        var p = 40m + 7m * d + (isExpress ? 20m : 0m);
        return Math.Round(p / 5m) * 5m;   // nearest ₹5
    }

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

    public static RiderTaskDto ToDto(DeliveryAssignment da, Order? o, Customer? c, CustomerAddress? addr)
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
            Payout:        EstimatePayout(da.DistanceKm, isExpress),
            Lat:           lat,
            Lng:           lng,
            SequenceNumber: da.SequenceNumber,
            CompletedAt:   da.CompletedAt);
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

        var tasks = rows
            .Select(x => RiderTaskMapper.ToDto(x.da, x.o, x.c, AddrFor(x.da, x.o)))
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
        ["started", "arrived", "completed", "failed"];

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

        // Any leg with an OTP (pickup or delivery) must be verified before completing.
        var legOtp = da.LegType == "pickup" ? o?.PickupOtp : o?.DeliveryOtp;
        if (cmd.Status == "completed"
            && !string.IsNullOrWhiteSpace(legOtp)
            && !da.OtpVerified)
            return RiderTaskResult.Conflict("OTP must be verified before completing.");

        var now = DateTimeOffset.UtcNow;
        da.Status = cmd.Status;
        switch (cmd.Status)
        {
            case "started":   da.StartedAt   ??= now; break;
            case "arrived":   da.ArrivedAt   ??= now; break;
            case "completed": da.CompletedAt ??= now; break;
        }
        da.UpdatedAt = now;
        da.UpdatedBy = cmd.UserId;
        await _db.SaveChangesAsync(ct);

        return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr));
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

        if (ok) da.OtpVerified = true;
        await _db.SaveChangesAsync(ct);

        if (!ok) return RiderTaskResult.Conflict("Incorrect OTP.");

        var c = o is not null ? await _db.Customers.FirstOrDefaultAsync(x => x.Id == o.CustomerId, ct) : null;
        var addrId = o is null ? (Guid?)null : (da.LegType == "pickup" ? o.PickupAddressId : o.DeliveryAddressId);
        var addr = addrId.HasValue
            ? await _db.CustomerAddresses.FirstOrDefaultAsync(a => a.Id == addrId.Value, ct)
            : null;

        return RiderTaskResult.Ok(RiderTaskMapper.ToDto(da, o, c, addr));
    }
}
