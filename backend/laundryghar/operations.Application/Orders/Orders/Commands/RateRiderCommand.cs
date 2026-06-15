using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Logistics;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Orders.Orders.Commands;

/// <summary>
/// Customer rates the RIDER who delivered their order — separate from the order rating.
/// Idempotent per (rider, order, customer); re-rating updates the score. Maintains the
/// rider's rating_average/rating_count aggregate. Allowed only for delivered/closed orders.
/// </summary>
public sealed record RateRiderCommand(Guid OrderId, Guid CustomerId, int Score, string? Comment)
    : ICommand<RateRiderResult>;

public enum RateRiderResultKind { Ok, NotFound, InvalidStatus, NoRider }
public sealed record RateRiderResult(RateRiderResultKind Kind, decimal? RiderAverage = null, int RiderCount = 0);

public sealed class RateRiderHandler : ICommandHandler<RateRiderCommand, RateRiderResult>
{
    private static readonly string[] Rateable = ["delivered", "closed"];
    private readonly IOperationsDbContext _db;
    public RateRiderHandler(IOperationsDbContext db) => _db = db;

    public async Task<RateRiderResult> HandleAsync(RateRiderCommand cmd, CancellationToken ct)
    {
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId && o.CustomerId == cmd.CustomerId, ct);
        if (order is null || order.DeletedAt != null) return new(RateRiderResultKind.NotFound);
        if (!Rateable.Contains(order.Status)) return new(RateRiderResultKind.InvalidStatus);

        var riderId = order.DeliveryRiderId ?? order.PickupRiderId;
        if (riderId is null) return new(RateRiderResultKind.NoRider);

        var now = DateTimeOffset.UtcNow;

        var existing = await _db.RiderRatings.FirstOrDefaultAsync(
            rr => rr.RiderId == riderId.Value && rr.OrderId == order.Id && rr.CustomerId == cmd.CustomerId, ct);
        if (existing is not null)
        {
            existing.Rating = (short)cmd.Score;
            existing.Comment = cmd.Comment;
        }
        else
        {
            _db.RiderRatings.Add(new RiderRating
            {
                Id = Guid.NewGuid(),
                RiderId = riderId.Value,
                BrandId = order.BrandId,
                OrderId = order.Id,
                CustomerId = cmd.CustomerId,
                LegType = order.DeliveryRiderId is not null ? "delivery" : "pickup",
                Rating = (short)cmd.Score,
                Comment = cmd.Comment,
                CreatedAt = now,
                CreatedBy = cmd.CustomerId,
            });
        }
        await _db.SaveChangesAsync(ct);

        // Recompute the rider's aggregate from the audit rows.
        var stats = await _db.RiderRatings.AsNoTracking()
            .Where(rr => rr.RiderId == riderId.Value)
            .GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(x => (double)x.Rating), Cnt = g.Count() })
            .FirstOrDefaultAsync(ct);

        var rider = await _db.Riders.FirstOrDefaultAsync(r => r.Id == riderId.Value, ct);
        if (rider is not null && stats is not null)
        {
            rider.RatingAverage = Math.Round((decimal)stats.Avg, 2);
            rider.RatingCount = stats.Cnt;
            rider.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        return new(RateRiderResultKind.Ok, rider?.RatingAverage, rider?.RatingCount ?? 0);
    }
}

public sealed class RateRiderValidator : AbstractValidator<RateRiderCommand>
{
    public RateRiderValidator()
    {
        RuleFor(x => x.Score).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}
