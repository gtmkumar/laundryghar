using laundryghar.Warehouse.Application.Board.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.Board.Queries;

/// <summary>Read model for the warehouse kanban — per-stage garment cards
/// enriched with item / fabric / customer names plus header metrics. Thin
/// read projection (no command side), brand-scoped (defence-in-depth).</summary>
public sealed record GetWarehouseBoardQuery : IRequest<WarehouseBoardDto>;

public sealed class GetWarehouseBoardHandler : IRequestHandler<GetWarehouseBoardQuery, WarehouseBoardDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    public GetWarehouseBoardHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    // Active "in flight" stages shown on the board, left → right.
    private static readonly (string Stage, string Label)[] Lanes =
    [
        (GarmentStage.Received, "Received"),
        (GarmentStage.Sorting,  "Sorting"),
        (GarmentStage.Washing,  "Washing"),
        (GarmentStage.Drying,   "Drying"),
        (GarmentStage.Ironing,  "Ironing"),
        (GarmentStage.Qc,       "QC"),
    ];

    public async Task<WarehouseBoardDto> Handle(GetWarehouseBoardQuery request, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var stages  = Lanes.Select(l => l.Stage).ToArray();

        var rows = await _db.Garments
            .Where(g => g.BrandId == brandId && stages.Contains(g.CurrentStage))
            .OrderByDescending(g => g.LastScannedAt)
            .Select(g => new
            {
                g.Id,
                g.TagCode,
                g.CurrentStage,
                g.LastScannedAt,
                Flagged    = g.IsDesignerWear || g.RewashCount > 0,
                ItemName   = g.Item != null ? g.Item.Name : null,
                FabricName = g.FabricType != null ? g.FabricType.Name : null,
                Display    = g.Customer != null ? g.Customer.DisplayName : null,
                First      = g.Customer != null ? g.Customer.FirstName : null,
                Last       = g.Customer != null ? g.Customer.LastName : null,
            })
            .ToListAsync(ct);

        var cards = rows.Select(r => new GarmentCardDto(
            r.Id,
            r.TagCode,
            string.IsNullOrWhiteSpace(r.ItemName) ? "Garment" : r.ItemName!,
            string.IsNullOrWhiteSpace(r.FabricName) ? "—" : r.FabricName!,
            FormatCustomer(r.Display, r.First, r.Last),
            r.CurrentStage,
            r.LastScannedAt,
            r.Flagged)).ToList();

        var byStage = cards.ToLookup(c => c.Stage);
        var columns = Lanes.Select(l =>
        {
            var laneCards = byStage[l.Stage].ToList();
            return new StageColumnDto(l.Stage, l.Label, laneCards.Count, laneCards);
        }).ToList();

        var inFlight = rows.Count;

        // Single processing warehouse for the brand (demo has one; pick the oldest).
        var wh = await _db.Warehouses
            .Where(w => w.BrandId == brandId)
            .OrderBy(w => w.CreatedAt)
            .Select(w => new { w.Id, w.Name, w.Code, w.DailyThroughputTarget })
            .FirstOrDefaultAsync(ct);

        var target = wh?.DailyThroughputTarget ?? 0;
        var capacityPct = target > 0 ? (int)Math.Round(inFlight * 100.0 / target) : 0;

        // Real measured throughput: garments completed since local (IST) midnight.
        // Compare as a UTC instant — Npgsql only writes offset-0 to timestamptz.
        var nowIst = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330));
        var istMidnightUtc = new DateTimeOffset(nowIst.Year, nowIst.Month, nowIst.Day, 0, 0, 0, TimeSpan.FromMinutes(330))
            .ToUniversalTime();
        var throughputToday = await _db.Garments
            .CountAsync(g => g.BrandId == brandId
                          && g.ActualCompletionAt != null
                          && g.ActualCompletionAt >= istMidnightUtc, ct);

        var summary = new WarehouseBoardSummaryDto(
            wh?.Id,
            wh?.Name ?? "Warehouse",
            wh?.Code ?? "",
            inFlight,
            capacityPct,
            target,
            throughputToday);

        return new WarehouseBoardDto(summary, columns);
    }

    private static string FormatCustomer(string? display, string? first, string? last)
    {
        if (!string.IsNullOrWhiteSpace(display)) return display!.Trim();
        var name = $"{first} {last}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "Walk-in" : name;
    }
}
