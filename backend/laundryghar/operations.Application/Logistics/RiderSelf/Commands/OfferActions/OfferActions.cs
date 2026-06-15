using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Logistics.Common;

namespace operations.Application.Logistics.RiderSelf.Commands.OfferActions;

// ── Offer accept / decline (offer_accept dispatch mode) ─────────────────────────

/// <summary>Outcome of a rider acting on an offered job.</summary>
public enum OfferActionOutcome { NotFound, Expired, Taken, Ok }

public sealed record OfferActionResult(OfferActionOutcome Outcome, Guid AssignmentId, string Status);

/// <summary>
/// Rider accepts an offered pickup (offer_accept dispatch). Wins the row by a guarded
/// transition offered→accepted, expires sibling offers for the same pickup, stamps the
/// pickup 'assigned', and increments the rider's load. IDOR-guarded by rider + brand.
/// </summary>
public sealed record AcceptOfferCommand(Guid AssignmentId, Guid UserId, Guid BrandId)
    : ICommand<OfferActionResult>;

public sealed class AcceptOfferHandler : ICommandHandler<AcceptOfferCommand, OfferActionResult>
{
    private readonly IOperationsDbContext _db;
    public AcceptOfferHandler(IOperationsDbContext db) => _db = db;

    public async Task<OfferActionResult> HandleAsync(AcceptOfferCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;
        var now = DateTimeOffset.UtcNow;

        // Self-resolve the rider from user_id + brand_id.
        var riderId = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (riderId is null)
            return new OfferActionResult(OfferActionOutcome.NotFound, cmd.AssignmentId, "not_found");

        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId
                                   && a.RiderId == riderId.Value
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

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            da.Status     = DeliveryAssignmentStatus.Accepted;
            da.AcceptedAt = now;
            da.UpdatedAt  = now;

            if (da.PickupRequestId is Guid pid)
            {
                // Expire the other live offers for this pickup.
                var siblings = await _db.DeliveryAssignments
                    .Where(x => x.PickupRequestId == pid && x.Id != da.Id
                             && x.Status == DeliveryAssignmentStatus.Offered)
                    .ToListAsync(innerCt);
                foreach (var s in siblings) { s.Status = DeliveryAssignmentStatus.Expired; s.UpdatedAt = now; }

                var pr = await _db.PickupRequests.FirstOrDefaultAsync(p => p.Id == pid, innerCt);
                if (pr is not null && pr.Status == "pending") { pr.Status = "assigned"; pr.UpdatedAt = now; }
            }

            await _db.SaveChangesAsync(innerCt);
        }, ct);

        await RiderLoad.IncrementAsync(_db, riderId.Value, ct);
        return new OfferActionResult(OfferActionOutcome.Ok, da.Id, da.Status);
    }
}

/// <summary>
/// Rider declines an offered pickup. Marks the offer rejected so the next dispatch cycle
/// re-offers to another rider (the decliner is excluded). IDOR-guarded by rider + brand.
/// </summary>
public sealed record DeclineOfferCommand(Guid AssignmentId, Guid UserId, Guid BrandId)
    : ICommand<OfferActionResult>;

public sealed class DeclineOfferHandler : ICommandHandler<DeclineOfferCommand, OfferActionResult>
{
    private readonly IOperationsDbContext _db;
    public DeclineOfferHandler(IOperationsDbContext db) => _db = db;

    public async Task<OfferActionResult> HandleAsync(DeclineOfferCommand command, CancellationToken cancellationToken)
    {
        var ct  = cancellationToken;
        var cmd = command;

        // Self-resolve the rider from user_id + brand_id.
        var riderId = await _db.Riders
            .Where(r => r.UserId == cmd.UserId && r.BrandId == cmd.BrandId)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (riderId is null)
            return new OfferActionResult(OfferActionOutcome.NotFound, cmd.AssignmentId, "not_found");

        var da = await _db.DeliveryAssignments
            .FirstOrDefaultAsync(a => a.Id == cmd.AssignmentId
                                   && a.RiderId == riderId.Value
                                   && a.BrandId == cmd.BrandId, ct);

        if (da is null || da.Status != DeliveryAssignmentStatus.Offered)
            return new OfferActionResult(OfferActionOutcome.NotFound, cmd.AssignmentId, "not_found");

        da.Status    = DeliveryAssignmentStatus.Rejected;
        da.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new OfferActionResult(OfferActionOutcome.Ok, da.Id, da.Status);
    }
}
