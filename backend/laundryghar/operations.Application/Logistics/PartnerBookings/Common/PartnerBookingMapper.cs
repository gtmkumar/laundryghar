using System.Text.Json;
using laundryghar.SharedDataModel.Entities.Logistics;
using operations.Application.Logistics.PartnerBookings.Dtos;

namespace operations.Application.Logistics.PartnerBookings.Common;

/// <summary>Serialization + projection helpers for RaaS partner bookings. The pickup/drop
/// snapshots are persisted as jsonb strings; these helpers keep serialize/deserialize consistent
/// between the create and list paths.</summary>
internal static class PartnerBookingMapper
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes a location snapshot to a compact JSON string for the jsonb column.</summary>
    internal static string Serialize(PartnerBookingLocation location) =>
        JsonSerializer.Serialize(location, Json);

    /// <summary>Deserializes a stored jsonb snapshot back to a structured location (null-safe).</summary>
    internal static PartnerBookingLocation? Deserialize(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        try
        {
            return JsonSerializer.Deserialize<PartnerBookingLocation>(snapshot, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static PartnerBookingDto ToDto(PartnerBooking e) => new(
        e.Id,
        e.PartnerId,
        e.BrandId,
        e.CreatedByPartnerUserId,
        Deserialize(e.PickupSnapshot),
        Deserialize(e.DropSnapshot),
        e.QuotedFare,
        e.Status,
        e.CreatedAt,
        e.UpdatedAt);
}
