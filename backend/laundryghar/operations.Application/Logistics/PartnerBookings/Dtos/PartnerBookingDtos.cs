namespace operations.Application.Logistics.PartnerBookings.Dtos;

/// <summary>Minimal pickup/drop snapshot for a RaaS partner booking. Stored as a jsonb column on
/// partner_bookings — there is no separate address entity in the MVP.</summary>
public sealed record PartnerBookingLocation(
    string? Address,
    string? ContactName,
    string? ContactPhone,
    double? Lat,
    double? Lng
);

/// <param name="Pickup">Where the goods are collected.</param>
/// <param name="Drop">Where the goods are delivered.</param>
/// <param name="QuotedFare">Optional fare quoted to the partner at booking time.</param>
/// <param name="BrandId">Optional brand whose rider fleet should serve the booking (soft reference).</param>
public sealed record CreatePartnerBookingRequest(
    PartnerBookingLocation Pickup,
    PartnerBookingLocation Drop,
    decimal? QuotedFare = null,
    Guid? BrandId = null
);

public sealed record PartnerBookingDto(
    Guid Id,
    Guid PartnerId,
    Guid? BrandId,
    Guid CreatedByPartnerUserId,
    PartnerBookingLocation? Pickup,
    PartnerBookingLocation? Drop,
    decimal? QuotedFare,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
