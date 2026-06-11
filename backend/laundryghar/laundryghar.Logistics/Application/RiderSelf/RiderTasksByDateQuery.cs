using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.Logistics.Application.Payout;
using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── Get my tasks for a specific calendar date ──────────────────────────────────
//
// Supports the earnings drill-down: rider taps a past day in the earnings
// screen and the app calls GET /api/v1/rider/tasks?date=YYYY-MM-DD.
//
// Date bucketing uses Asia/Kolkata (IST, UTC+05:30) — matching the rider app's
// local-date bucketing. A task completed at 23:45 IST on 2026-06-10 is in the
// 2026-06-10 bucket server-side, same as on device.
//
// The same RiderTaskDto shape and sort order as /tasks/today is returned
// (re-uses RiderTaskMapper.ToDto, same LINQ join, same address hydration).
// Only completed/failed tasks are returned for past dates; for today the caller
// should use /tasks/today which also shows open tasks.
// ──────────────────────────────────────────────────────────────────────────────

public sealed record GetMyTasksByDateQuery(
    Guid   UserId,
    Guid   BrandId,
    DateOnly Date          // calendar date in IST; stored as UTC boundary pair in query
) : IRequest<List<RiderTaskDto>>;

public sealed class GetMyTasksByDateHandler
    : IRequestHandler<GetMyTasksByDateQuery, List<RiderTaskDto>>
{
    private readonly LaundryGharDbContext _db;

    // IST offset: +05:30
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    public GetMyTasksByDateHandler(LaundryGharDbContext db) => _db = db;

    public async Task<List<RiderTaskDto>> Handle(
        GetMyTasksByDateQuery q, CancellationToken ct)
    {
        var rider = await _db.Riders
            .Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return [];

        // Convert the IST calendar date to a UTC time window.
        // Date 2026-06-10 IST starts at 2026-06-09T18:30:00Z and ends at 2026-06-10T18:30:00Z.
        //
        // Npgsql requires UTC (offset 0) DateTimeOffset values for timestamptz parameters.
        // Passing a +05:30-offset value throws:
        //   "Cannot write DateTimeOffset with Offset=05:30:00 ... only offset 0 (UTC) is supported."
        // Derive the correct UTC bounds by constructing in IST then converting to UTC so
        // the IST day-boundary logic is preserved without violating the Npgsql constraint.
        var startUtc = new DateTimeOffset(q.Date.ToDateTime(TimeOnly.MinValue), IstOffset)
                           .ToUniversalTime();
        var endUtc   = startUtc.AddDays(1);

        // For historical dates we return only terminal (completed/failed) legs;
        // active open tasks don't exist in the past.
        var rows = await (
            from da in _db.DeliveryAssignments
            where da.RiderId == rider.Id
               && da.BrandId  == q.BrandId
               && (da.Status == "completed" || da.Status == "failed")
               && da.CompletedAt >= startUtc
               && da.CompletedAt <  endUtc
            join o in _db.Orders
                on new { I = da.OrderId, C = da.OrderCreatedAt }
                equals new { I = (Guid?)o.Id, C = (DateTimeOffset?)o.CreatedAt }
                into oj
            from o in oj.DefaultIfEmpty()
            join c in _db.Customers on o.CustomerId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            select new { da, o, c })
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        // Hydrate addresses (same pattern as GetMyTasksTodayHandler)
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

        return rows
            .Select(x => RiderTaskMapper.ToDto(x.da, x.o, x.c, AddrFor(x.da, x.o), payoutCfg))
            .OrderBy(t => t.CompletedAt)
            .ToList();
    }
}
