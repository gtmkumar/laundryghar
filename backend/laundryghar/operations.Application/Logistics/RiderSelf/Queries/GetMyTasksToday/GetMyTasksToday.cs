using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Common;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.Application.Logistics.RiderSelf.Queries.GetMyTasksToday;

// ── Get my tasks for today ─────────────────────────────────────────────────────

public sealed record GetMyTasksTodayQuery(Guid UserId, Guid BrandId) : IQuery<List<RiderTaskDto>>;

public sealed class GetMyTasksTodayHandler : IQueryHandler<GetMyTasksTodayQuery, List<RiderTaskDto>>
{
    private readonly IOperationsDbContext _db;
    public GetMyTasksTodayHandler(IOperationsDbContext db) => _db = db;

    public async Task<List<RiderTaskDto>> HandleAsync(GetMyTasksTodayQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var q  = query;
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
