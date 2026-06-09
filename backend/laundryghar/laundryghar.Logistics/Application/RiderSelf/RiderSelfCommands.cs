using laundryghar.Logistics.Application.Assignments.Commands;
using laundryghar.Logistics.Application.Assignments.Dtos;
using laundryghar.Logistics.Application.Riders.Commands;
using laundryghar.Logistics.Application.Riders.Dtos;
using laundryghar.SharedDataModel.Enums;
using MediatR;
using NetTopologySuite.Geometries;

namespace laundryghar.Logistics.Application.RiderSelf;

// ── Get own rider profile ─────────────────────────────────────────────────────

public sealed record GetMyRiderProfileQuery(Guid UserId) : IRequest<RiderDto?>;

public sealed class GetMyRiderProfileHandler : IRequestHandler<GetMyRiderProfileQuery, RiderDto?>
{
    private readonly LaundryGharDbContext _db;
    public GetMyRiderProfileHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderDto?> Handle(GetMyRiderProfileQuery q, CancellationToken ct)
    {
        var r = await _db.Riders.FirstOrDefaultAsync(x => x.UserId == q.UserId, ct);
        if (r is null) return null;
        return await CreateRiderHandler.LoadEnrichedAsync(_db, r, ct);
    }
}

// ── Get own assignments for today ─────────────────────────────────────────────

public sealed record GetMyAssignmentsTodayQuery(Guid UserId, Guid BrandId)
    : IRequest<List<RiderAssignmentDto>>;

public sealed class GetMyAssignmentsTodayHandler
    : IRequestHandler<GetMyAssignmentsTodayQuery, List<RiderAssignmentDto>>
{
    private readonly LaundryGharDbContext _db;
    public GetMyAssignmentsTodayHandler(LaundryGharDbContext db) => _db = db;

    public async Task<List<RiderAssignmentDto>> Handle(GetMyAssignmentsTodayQuery q, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

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

// ── Batch location ping ───────────────────────────────────────────────────────

public sealed record BatchLocationPingCommand(
    Guid                   RiderId,
    Guid                   BrandId,
    Guid?                  CreatedBy,
    List<LocationPingInput> Pings) : IRequest<PingBatchResponse>;

public sealed class BatchLocationPingHandler : IRequestHandler<BatchLocationPingCommand, PingBatchResponse>
{
    private readonly LaundryGharDbContext _db;
    private static readonly GeometryFactory GeoFactory =
        NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public BatchLocationPingHandler(LaundryGharDbContext db) => _db = db;

    public async Task<PingBatchResponse> Handle(BatchLocationPingCommand cmd, CancellationToken ct)
    {
        if (cmd.Pings.Count == 0) return new PingBatchResponse(0);

        var now = DateTimeOffset.UtcNow;
        var pings = cmd.Pings.Select(p =>
        {
            // WGS-84 Point: X=longitude, Y=latitude
            var point = GeoFactory.CreatePoint(new Coordinate(p.Longitude, p.Latitude));

            return new RiderLocationPing
            {
                Id                  = Guid.NewGuid(),
                PingedAt            = p.PingedAt,
                RiderId             = cmd.RiderId,
                BrandId             = cmd.BrandId,
                Location            = point,
                AccuracyMeters      = p.AccuracyMeters,
                SpeedKmph           = p.SpeedKmph,
                HeadingDegrees      = p.HeadingDegrees,
                BatteryPercent      = p.BatteryPercent,
                IsMoving            = p.IsMoving,
                ActivityType        = p.ActivityType,
                CurrentAssignmentId = p.CurrentAssignmentId,
                Metadata            = null,
                CreatedAt           = now,
                CreatedBy           = cmd.CreatedBy
            };
        }).ToList();

        _db.RiderLocationPings.AddRange(pings);
        await _db.SaveChangesAsync(ct);

        // Update rider's last known location from the most recent ping
        var latest = cmd.Pings.MaxBy(p => p.PingedAt);
        if (latest is not null)
        {
            var rider = await _db.Riders.FirstOrDefaultAsync(r => r.Id == cmd.RiderId, ct);
            if (rider is not null)
            {
                rider.LastKnownLocation = GeoFactory.CreatePoint(
                    new Coordinate(latest.Longitude, latest.Latitude));
                rider.LastPingAt = latest.PingedAt;
                rider.UpdatedAt  = now;
                await _db.SaveChangesAsync(ct);
            }

            // Geofence auto-status: reaching the customer flips started→arrived, and a
            // collected pickup reaching the store gets dropped_at stamped.
            await GeofenceEvaluator.EvaluateAsync(
                _db, cmd.RiderId, cmd.BrandId, latest.Latitude, latest.Longitude, now, ct);
        }

        return new PingBatchResponse(pings.Count);
    }
}

// ── Update own assignment status ──────────────────────────────────────────────

public sealed record UpdateMyAssignmentStatusCommand(
    Guid   AssignmentId,
    Guid   RiderId,
    Guid   BrandId,
    string Status) : IRequest<RiderAssignmentDto?>;

public sealed class UpdateMyAssignmentStatusHandler
    : IRequestHandler<UpdateMyAssignmentStatusCommand, RiderAssignmentDto?>
{
    private readonly LaundryGharDbContext _db;
    public UpdateMyAssignmentStatusHandler(LaundryGharDbContext db) => _db = db;

    public async Task<RiderAssignmentDto?> Handle(UpdateMyAssignmentStatusCommand cmd, CancellationToken ct)
    {
        // Find the assignment — must belong to this rider AND this brand (self-filter)
        var assignment = await _db.RiderAssignments
            .FirstOrDefaultAsync(a => a.Id    == cmd.AssignmentId
                                   && a.RiderId == cmd.RiderId
                                   && a.BrandId == cmd.BrandId, ct);

        // Return null → endpoint returns 404 (assignment not found OR does not belong to this rider)
        if (assignment is null) return null;

        assignment.Status    = cmd.Status;
        assignment.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return CreateRiderAssignmentHandler.ToDto(assignment);
    }
}
