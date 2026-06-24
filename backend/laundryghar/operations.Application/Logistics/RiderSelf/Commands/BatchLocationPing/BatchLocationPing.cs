using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Common;
using operations.Application.Logistics.RiderSelf.Dtos;
using laundryghar.SharedDataModel.Entities.Logistics;

namespace operations.Application.Logistics.RiderSelf.Commands.BatchLocationPing;

// ── Batch location ping ───────────────────────────────────────────────────────

/// <summary>
/// Batch-persists rider GPS pings. The rider is self-resolved from <c>UserId</c> + <c>BrandId</c>;
/// when no rider profile matches, the batch is a no-op (0 accepted) — matching the legacy
/// endpoint's 404-on-missing-rider behaviour (the endpoint maps an empty resolution to NotFound).
/// </summary>
public sealed record BatchLocationPingCommand(
    Guid                   UserId,
    Guid                   BrandId,
    List<LocationPingInput> Pings) : ICommand<PingBatchResponse>;

public sealed class BatchLocationPingHandler : ICommandHandler<BatchLocationPingCommand, PingBatchResponse>
{
    private readonly IOperationsDbContext _db;
    private readonly Fulfillment.IFulfillmentStrategyResolver _strategies;
    private static readonly GeometryFactory GeoFactory =
        NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public BatchLocationPingHandler(IOperationsDbContext db, Fulfillment.IFulfillmentStrategyResolver strategies)
    {
        _db = db;
        _strategies = strategies;
    }

    public async Task<PingBatchResponse> HandleAsync(BatchLocationPingCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;
        if (cmd.Pings.Count == 0) return new PingBatchResponse(0);

        // Self-resolve the rider from user_id + brand_id.
        var riderId = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (riderId is null) return new PingBatchResponse(0);

        var now = DateTimeOffset.UtcNow;
        var pings = cmd.Pings.Select(p =>
        {
            // WGS-84 Point: X=longitude, Y=latitude
            var point = GeoFactory.CreatePoint(new Coordinate(p.Longitude, p.Latitude));

            return new RiderLocationPing
            {
                Id                  = Guid.NewGuid(),
                PingedAt            = p.PingedAt,
                RiderId             = riderId.Value,
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
                CreatedBy           = cmd.UserId
            };
        }).ToList();

        _db.RiderLocationPings.AddRange(pings);
        await _db.SaveChangesAsync(ct);

        // Update rider's last known location from the most recent ping
        var latest = cmd.Pings.MaxBy(p => p.PingedAt);
        if (latest is not null)
        {
            var rider = await _db.Riders.FirstOrDefaultAsync(r => r.Id == riderId.Value, ct);
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
                _db, _strategies, riderId.Value, cmd.BrandId, latest.Latitude, latest.Longitude, now, ct);
        }

        return new PingBatchResponse(pings.Count);
    }
}
