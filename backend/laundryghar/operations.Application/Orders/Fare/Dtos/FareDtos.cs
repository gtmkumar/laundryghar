namespace operations.Application.Orders.Fare.Dtos;

/// <summary>Request for a delivery fare quote (point-to-point parcel pricing).</summary>
public sealed record FareQuoteRequest(
    Guid PickupAddressId,
    Guid DeliveryAddressId,
    /// <summary>Required vehicle tier — see <see cref="laundryghar.SharedDataModel.Enums.VehicleTier"/>.
    /// Null lets the engine price the default (two_wheeler) tier.</summary>
    string? VehicleTier = null,
    bool IsExpress = false);

/// <summary>
/// A held fare quote. <see cref="Token"/> is replayed at order creation to lock in the
/// price; it expires at <see cref="ExpiresAt"/>.
/// </summary>
public sealed record FareQuoteDto(
    decimal PickupCharge,
    decimal DeliveryCharge,
    decimal TotalCharge,
    decimal DistanceKm,
    decimal SurgeMultiplier,
    string? VehicleTier,
    DateTimeOffset ExpiresAt,
    string Token);
