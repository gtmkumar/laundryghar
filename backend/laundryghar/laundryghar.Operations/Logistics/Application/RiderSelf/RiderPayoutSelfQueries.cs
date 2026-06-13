using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
using FluentValidation;
using laundryghar.Logistics.Application.RiderOps;
using MediatR;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── DTOs ─────────────────────────────────────────────────────────────────────

/// <summary>One calendar-day earnings bucket.</summary>
public sealed record RiderPayoutDayDto(
    DateOnly Date,
    int TaskCount,
    decimal TotalPayout);

/// <summary>Earnings summary + per-day breakdown for the requested period.</summary>
public sealed record RiderPayoutSummaryDto(
    decimal TotalPayout,
    decimal AvgPerTask,
    int Days,
    IReadOnlyList<RiderPayoutDayDto> Breakdown);

// ── Query + Validator ────────────────────────────────────────────────────────

/// <param name="Days">Rolling window in calendar days — supported values: 7 or 30.</param>
public sealed record GetMyPayoutsQuery(Guid UserId, Guid BrandId, int Days)
    : IRequest<RiderPayoutSummaryDto?>;

public sealed class GetMyPayoutsQueryValidator : AbstractValidator<GetMyPayoutsQuery>
{
    public GetMyPayoutsQueryValidator()
    {
        RuleFor(q => q.Days)
            .InclusiveBetween(1, 90)
            .WithMessage("'days' must be between 1 and 90.");
    }
}

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetMyPayoutsHandler : IRequestHandler<GetMyPayoutsQuery, RiderPayoutSummaryDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetMyPayoutsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderPayoutSummaryDto?> Handle(GetMyPayoutsQuery q, CancellationToken ct)
    {
        var rider = await _db.Riders
            .Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId)
            .Select(r => new { r.Id })
            .FirstOrDefaultAsync(ct);
        if (rider is null) return null;

        // "today" in IST so the per-day grouping aligns with the ops board.
        var today = RiderOpsTime.TodayIst();
        var fromDay = today.AddDays(-(q.Days - 1));  // inclusive start
        var (startUtc, endUtc) = RiderOpsTime.IstRangeUtc(fromDay, today);

        // Only completed legs with a persisted payout contribute. We group by
        // IST calendar day derived from completed_at (UTC in DB, converted client-side).
        var legs = await _db.DeliveryAssignments.AsNoTracking()
            .Where(d => d.BrandId == q.BrandId && d.RiderId == rider.Id
                     && d.Status == "completed"
                     && d.CompletedAt >= startUtc && d.CompletedAt < endUtc
                     && d.PayoutAmount != null)
            .Select(d => new { d.CompletedAt, d.PayoutAmount })
            .ToListAsync(ct);

        // Group in memory by IST calendar day.
        var istOffset = TimeSpan.FromHours(5.5);
        var grouped = legs
            .GroupBy(l => DateOnly.FromDateTime(l.CompletedAt!.Value.ToOffset(istOffset).Date))
            .Select(g => new RiderPayoutDayDto(g.Key, g.Count(), g.Sum(x => x.PayoutAmount ?? 0m)))
            .OrderByDescending(d => d.Date)
            .ToList();

        var totalPayout = grouped.Sum(d => d.TotalPayout);
        var totalTasks  = grouped.Sum(d => d.TaskCount);
        var avgPerTask  = totalTasks > 0 ? Math.Round(totalPayout / totalTasks, 2) : 0m;

        return new RiderPayoutSummaryDto(totalPayout, avgPerTask, q.Days, grouped);
    }
}
