using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Orders.Fare.Dtos;

namespace operations.Application.Orders.Fare.Queries;

/// <summary>
/// Computes a delivery fare quote between two of the customer's addresses and returns a
/// signed, TTL-bound token that locks the price in at order creation. CustomerId + BrandId
/// are derived from the JWT by the endpoint (never the request body) — both addresses must
/// belong to that customer/brand (IDOR guard).
/// </summary>
public sealed record GetFareQuoteQuery(Guid CustomerId, Guid BrandId, FareQuoteRequest Request)
    : IQuery<FareQuoteDto>;

public sealed class GetFareQuoteHandler : IQueryHandler<GetFareQuoteQuery, FareQuoteDto>
{
    private readonly IOperationsDbContext _db;
    private readonly IFieldCipher _cipher;

    public GetFareQuoteHandler(IOperationsDbContext db, IFieldCipher cipher)
    {
        _db = db;
        _cipher = cipher;
    }

    public async Task<FareQuoteDto> HandleAsync(GetFareQuoteQuery q, CancellationToken ct)
    {
        var req = q.Request;

        if (req.VehicleTier is not null && !VehicleTier.IsValid(req.VehicleTier))
            throw new BusinessRuleException(
                $"VehicleTier must be one of: {string.Join(", ", VehicleTier.All)}.");

        if (req.PickupAddressId == req.DeliveryAddressId)
            throw new BusinessRuleException("Pickup and drop addresses must be different.");

        // Load both endpoints, IDOR-guarded by customer + brand. Project the NTS Point;
        // read .Y/.X client-side (geography rejects ST_Y/ST_X server-side).
        var addrs = await _db.CustomerAddresses.AsNoTracking()
            .Where(a => a.CustomerId == q.CustomerId
                     && a.BrandId == q.BrandId
                     && (a.Id == req.PickupAddressId || a.Id == req.DeliveryAddressId))
            .Select(a => new { a.Id, Point = a.GeoLocation })
            .ToListAsync(ct);

        var pickup = addrs.FirstOrDefault(a => a.Id == req.PickupAddressId)
            ?? throw new KeyNotFoundException("Pickup address not found.");
        var drop = addrs.FirstOrDefault(a => a.Id == req.DeliveryAddressId)
            ?? throw new KeyNotFoundException("Drop address not found.");

        if (pickup.Point is null || drop.Point is null)
            throw new BusinessRuleException(
                "Both pickup and drop addresses must have a geo-location to quote a fare.");

        var distanceKm = (decimal)GeoMath.HaversineKm(
            pickup.Point.Y, pickup.Point.X, drop.Point.Y, drop.Point.X);

        var settings = await FareConfig.LoadAsync(_db, q.BrandId, ct);
        var now = DateTimeOffset.UtcNow;
        var breakdown = settings.Compute(distanceKm, req.VehicleTier, now);

        var expiresAt = now.AddSeconds(settings.QuoteTtlSeconds);
        var token = FareQuoteToken.Issue(_cipher, new FareQuotePayload(
            req.PickupAddressId, req.DeliveryAddressId, req.VehicleTier,
            Math.Round(distanceKm, 2),
            breakdown.PickupCharge, breakdown.DeliveryCharge, breakdown.SurgeMultiplier,
            expiresAt.ToUnixTimeSeconds()));

        return new FareQuoteDto(
            breakdown.PickupCharge,
            breakdown.DeliveryCharge,
            breakdown.PickupCharge + breakdown.DeliveryCharge,
            Math.Round(distanceKm, 2),
            breakdown.SurgeMultiplier,
            req.VehicleTier,
            expiresAt,
            token);
    }
}
