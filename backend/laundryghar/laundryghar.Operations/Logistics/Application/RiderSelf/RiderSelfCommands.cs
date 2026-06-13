using laundryghar.Orders.Application.Common;
using laundryghar.Logistics.Infrastructure.Auth;
using laundryghar.Logistics.Infrastructure.Services;
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

// ── Offer accept / decline (offer_accept dispatch mode) ─────────────────────────

/// <summary>Outcome of a rider acting on an offered job.</summary>
public enum OfferActionOutcome { NotFound, Expired, Taken, Ok }

public sealed record OfferActionResult(OfferActionOutcome Outcome, Guid AssignmentId, string Status);

/// <summary>
/// Rider accepts an offered pickup (offer_accept dispatch). Wins the row by a guarded
/// transition offered→accepted, expires sibling offers for the same pickup, stamps the
/// pickup 'assigned', and increments the rider's load. IDOR-guarded by rider + brand.
/// </summary>
public sealed record AcceptOfferCommand(Guid AssignmentId, Guid RiderId, Guid BrandId)
    : IRequest<OfferActionResult>;

public sealed class AcceptOfferHandler : IRequestHandler<AcceptOfferCommand, OfferActionResult>
{
    private readonly LaundryGharDbContext _db;
    public AcceptOfferHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OfferActionResult> Handle(AcceptOfferCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId
                                   && a.RiderId == cmd.RiderId
                                   && a.BrandId == cmd.BrandId, ct);

        if (da is null || da.Status != DeliveryAssignmentStatus.Offered)
            return new OfferActionResult(OfferActionOutcome.NotFound, cmd.AssignmentId, "not_found");

        if (da.OfferExpiresAt is not null && da.OfferExpiresAt < now)
        {
            da.Status = DeliveryAssignmentStatus.Expired;
            da.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return new OfferActionResult(OfferActionOutcome.Expired, da.Id, da.Status);
        }

        // Sibling guard: another rider already holds an accepted/active assignment for this pickup.
        if (da.PickupRequestId is Guid pickupId)
        {
            var taken = await _db.DeliveryAssignments.AnyAsync(x =>
                x.PickupRequestId == pickupId && x.Id != da.Id &&
                (x.Status == DeliveryAssignmentStatus.Accepted
              || x.Status == DeliveryAssignmentStatus.Assigned
              || x.Status == DeliveryAssignmentStatus.Started
              || x.Status == DeliveryAssignmentStatus.Arrived
              || x.Status == DeliveryAssignmentStatus.Completed), ct);
            if (taken)
                return new OfferActionResult(OfferActionOutcome.Taken, da.Id, "taken");
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            da.Status     = DeliveryAssignmentStatus.Accepted;
            da.AcceptedAt = now;
            da.UpdatedAt  = now;

            if (da.PickupRequestId is Guid pid)
            {
                // Expire the other live offers for this pickup.
                var siblings = await _db.DeliveryAssignments
                    .Where(x => x.PickupRequestId == pid && x.Id != da.Id
                             && x.Status == DeliveryAssignmentStatus.Offered)
                    .ToListAsync(ct);
                foreach (var s in siblings) { s.Status = DeliveryAssignmentStatus.Expired; s.UpdatedAt = now; }

                var pr = await _db.PickupRequests.FirstOrDefaultAsync(p => p.Id == pid, ct);
                if (pr is not null && pr.Status == "pending") { pr.Status = "assigned"; pr.UpdatedAt = now; }
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        await SharedDataModel.Logistics.RiderLoadHelper.IncrementAsync(_db, cmd.RiderId, ct);
        return new OfferActionResult(OfferActionOutcome.Ok, da.Id, da.Status);
    }
}

/// <summary>
/// Rider declines an offered pickup. Marks the offer rejected so the next dispatch cycle
/// re-offers to another rider (the decliner is excluded). IDOR-guarded by rider + brand.
/// </summary>
public sealed record DeclineOfferCommand(Guid AssignmentId, Guid RiderId, Guid BrandId)
    : IRequest<OfferActionResult>;

public sealed class DeclineOfferHandler : IRequestHandler<DeclineOfferCommand, OfferActionResult>
{
    private readonly LaundryGharDbContext _db;
    public DeclineOfferHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OfferActionResult> Handle(DeclineOfferCommand cmd, CancellationToken ct)
    {
        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId
                                   && a.RiderId == cmd.RiderId
                                   && a.BrandId == cmd.BrandId, ct);

        if (da is null || da.Status != DeliveryAssignmentStatus.Offered)
            return new OfferActionResult(OfferActionOutcome.NotFound, cmd.AssignmentId, "not_found");

        da.Status    = DeliveryAssignmentStatus.Rejected;
        da.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new OfferActionResult(OfferActionOutcome.Ok, da.Id, da.Status);
    }
}
