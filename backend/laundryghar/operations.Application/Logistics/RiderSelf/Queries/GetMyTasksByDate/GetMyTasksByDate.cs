using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Common;
using operations.Application.Logistics.RiderSelf.Dtos;

namespace operations.Application.Logistics.RiderSelf.Queries.GetMyTasksByDate;

// ── Get my tasks for a specific calendar date (IST) — earnings drill-down ───────

public sealed record GetMyTasksByDateQuery(
    Guid   UserId,
    Guid   BrandId,
    DateOnly Date) : IQuery<List<RiderTaskDto>>;

public sealed class GetMyTasksByDateHandler
    : IQueryHandler<GetMyTasksByDateQuery, List<RiderTaskDto>>
{
    private readonly IOperationsDbContext _db;

    // IST offset: +05:30
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    public GetMyTasksByDateHandler(IOperationsDbContext db) => _db = db;

    public async Task<List<RiderTaskDto>> HandleAsync(GetMyTasksByDateQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var q  = query;
        var rider = await _db.Riders
            .Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return [];

        // Convert the IST calendar date to a UTC time window.
        // Npgsql requires UTC (offset 0) DateTimeOffset values for timestamptz parameters,
        // so construct in IST then convert to UTC to preserve the IST day boundary.
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
