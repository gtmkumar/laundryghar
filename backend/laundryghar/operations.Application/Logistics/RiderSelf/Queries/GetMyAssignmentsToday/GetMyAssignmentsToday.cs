using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Assignments.Commands.CreateRiderAssignment;
using operations.Application.Logistics.Assignments.Dtos;
using operations.Application.Logistics.Common;

namespace operations.Application.Logistics.RiderSelf.Queries.GetMyAssignmentsToday;

// ── Get own assignments for today ─────────────────────────────────────────────

public sealed record GetMyAssignmentsTodayQuery(Guid UserId, Guid BrandId)
    : IQuery<List<RiderAssignmentDto>>;

public sealed class GetMyAssignmentsTodayHandler
    : IQueryHandler<GetMyAssignmentsTodayQuery, List<RiderAssignmentDto>>
{
    private readonly IOperationsDbContext _db;
    public GetMyAssignmentsTodayHandler(IOperationsDbContext db) => _db = db;

    public async Task<List<RiderAssignmentDto>> HandleAsync(GetMyAssignmentsTodayQuery query, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var q  = query;

        // DEFECT 7: shift_date is a calendar day; "today" must be the rider's local
        // (IST/store-tz) day, not the UTC day. At 04:30 IST DateTime.UtcNow is still
        // yesterday, so the UTC-based DateOnly returned [] while tasks/today had work.
        var tz = LocalDateRange.Resolve(LocalDateRange.DefaultTimeZoneId);
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);

        var assignments = await _db.RiderAssignments
            .Where(a => a.BrandId == q.BrandId && a.ShiftDate == today)
            .Join(_db.Riders.Where(r => r.UserId == q.UserId && r.BrandId == q.BrandId),
                  a => a.RiderId, r => r.Id,
                  (a, r) => a)
            .OrderBy(a => a.ShiftStart)
            .ToListAsync(ct);

        return assignments.Select(CreateRiderAssignmentHandler.ToDto).ToList();
    }
}
