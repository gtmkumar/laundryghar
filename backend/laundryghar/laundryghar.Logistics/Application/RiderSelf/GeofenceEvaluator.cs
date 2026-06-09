using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Logistics.Application.RiderSelf;

/// <summary>
/// Auto-advances a rider's in-progress legs based on where they are. Called on
/// every location ping (with the rider's newest position). Two transitions:
///
///  1. <b>Reached the customer</b> — a leg in <c>started</c> whose customer geofence
///     the rider has entered becomes <c>arrived</c> (arrived_at stamped). This is
///     the "auto-update status when the rider reaches the site" behaviour.
///  2. <b>Dropped at the laundry</b> — a <c>pickup</c> leg that's been collected
///     (collected_at set) and whose rider has reached the store/warehouse geofence
///     gets dropped_at stamped (the second half of the pickup round-trip).
///
/// Geofencing is a plain haversine check against the leg's destination — the
/// customer point (delivery_assignments.geo_location) for the arrival, the store
/// point (stores.geo_location) for the drop. No PostGIS round-trip needed.
/// </summary>
internal static class GeofenceEvaluator
{
    /// <summary>Enter radius, metres. Generous enough for GPS jitter / parking nearby.</summary>
    internal const double RadiusMeters = 150;

    private const double EarthRadiusMeters = 6_371_000;

    internal static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        double ToRad(double d) => d * Math.PI / 180.0;
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>
    /// Evaluate the rider's active legs against their latest position and apply any
    /// geofence transitions. Returns the number of legs changed (0 if none).
    /// </summary>
    internal static async Task<int> EvaluateAsync(
        LaundryGharDbContext db, Guid riderId, Guid brandId, double lat, double lng,
        DateTimeOffset now, CancellationToken ct)
    {
        // The rider's currently in-progress legs (cheap; a rider has very few).
        var legs = await db.DeliveryAssignments
            .Where(d => d.RiderId == riderId && d.BrandId == brandId
                     && (d.Status == "started" || d.Status == "arrived"))
            .ToListAsync(ct);
        if (legs.Count == 0) return 0;

        // Store points for the drop geofence (pickup legs only) — one lookup.
        var storeIds = legs.Where(l => l.LegType == "pickup").Select(l => l.StoreId).Distinct().ToList();
        var storePts = storeIds.Count == 0
            ? new Dictionary<Guid, (double lat, double lng)>()
            : (await db.Stores.AsNoTracking()
                    .Where(s => storeIds.Contains(s.Id) && s.GeoLocation != null)
                    .Select(s => new { s.Id, s.GeoLocation })
                    .ToListAsync(ct))
                .ToDictionary(s => s.Id, s => (lat: s.GeoLocation!.Y, lng: s.GeoLocation!.X));

        var changed = 0;
        foreach (var leg in legs)
        {
            // 1. started → arrived when inside the customer geofence.
            if (leg.Status == "started" && leg.GeoLocation is { } cust
                && DistanceMeters(lat, lng, cust.Y, cust.X) <= RadiusMeters)
            {
                leg.Status = "arrived";
                leg.ArrivedAt ??= now;
                leg.UpdatedAt = now;
                changed++;
                continue;
            }

            // 2. pickup that's collected but not yet dropped → stamp dropped_at at the store.
            if (leg.LegType == "pickup" && leg.Status == "arrived"
                && leg.CollectedAt is not null && leg.DroppedAt is null
                && storePts.TryGetValue(leg.StoreId, out var store)
                && DistanceMeters(lat, lng, store.lat, store.lng) <= RadiusMeters)
            {
                leg.DroppedAt = now;
                leg.UpdatedAt = now;
                changed++;
            }
        }

        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }
}
