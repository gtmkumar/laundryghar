using laundryghar.SharedDataModel.Enums;

namespace commerce.Infrastructure.Worker.Services.AutoDispatch;

/// <summary>
/// A candidate rider for auto-dispatch ranking.
/// All fields are value-typed so this can be constructed and tested without a database.
/// </summary>
/// <param name="RiderId">Logistics rider primary key.</param>
/// <param name="CurrentLoad">Active assignment count at query time.</param>
/// <param name="DailyDeliveryCapacity">Max assignments permitted per day.</param>
/// <param name="RiderLat">Rider's last known latitude (null when no recent ping).</param>
/// <param name="RiderLng">Rider's last known longitude (null when no recent ping).</param>
/// <param name="VehicleType">Rider's vehicle tier (see <see cref="VehicleTier"/>); used for
/// tier-aware matching. Defaults to two_wheeler when unknown so existing callers/tests
/// that don't set it keep matching unconstrained jobs.</param>
/// <param name="SameFranchise">True when this rider belongs to the job's originating
/// franchise; ranked ahead of brand-wide riders so a franchise's own riders are
/// always preferred for its jobs.</param>
public sealed record RiderCandidate(
    Guid    RiderId,
    int     CurrentLoad,
    int     DailyDeliveryCapacity,
    double? RiderLat,
    double? RiderLng,
    string  VehicleType = VehicleTier.TwoWheeler,
    bool    SameFranchise = false);

/// <summary>
/// Pure static ranking logic — no database access, fully unit-testable.
///
/// Eligibility filter: is_on_duty=true, status='active', CurrentLoad &lt; DailyDeliveryCapacity,
/// and (when the job requires a tier) the rider's vehicle ranks at least that high.
/// Ranking:
///   1. SameFranchise descending (a franchise's own riders preferred for its jobs).
///   2. CurrentLoad ascending (least busy first).
///   3. Haversine distance to the request address ascending (when both rider and
///      request have coordinates). Candidates with missing coordinates are treated
///      as equidistant and rank after those with known distance.
/// </summary>
public static class RiderRanker
{
    /// <summary>
    /// From an already-filtered list of eligible candidates, picks the best one for
    /// the given request coordinates and (optional) required vehicle tier.
    ///
    /// Returns <c>null</c> if no candidate satisfies the tier requirement (or the list
    /// is empty).
    /// </summary>
    /// <param name="candidates">Candidates that have already passed core eligibility checks
    /// (on_duty=true, status=active, load &lt; capacity).</param>
    /// <param name="requestLat">Request address latitude — null when unknown.</param>
    /// <param name="requestLng">Request address longitude — null when unknown.</param>
    /// <param name="requiredTier">Vehicle tier the job requires, or null for no constraint.
    /// A rider qualifies when its vehicle ranks at least as high (upgrade ladder).</param>
    public static RiderCandidate? PickBest(
        IReadOnlyList<RiderCandidate> candidates,
        double? requestLat,
        double? requestLng,
        string? requiredTier = null)
    {
        // Tier gate first — a bigger vehicle may take a smaller job, never the reverse.
        var eligible = string.IsNullOrEmpty(requiredTier)
            ? candidates
            : candidates.Where(c => VehicleTier.CanServe(c.VehicleType, requiredTier)).ToList();

        if (eligible.Count == 0) return null;
        if (eligible.Count == 1) return eligible[0];

        bool hasRequestCoords = requestLat.HasValue && requestLng.HasValue;

        return eligible
            .OrderByDescending(c => c.SameFranchise)
            .ThenBy(c => c.CurrentLoad)
            .ThenBy(c =>
            {
                if (!hasRequestCoords || !c.RiderLat.HasValue || !c.RiderLng.HasValue)
                    return double.MaxValue; // No coords → rank last within same bucket
                return HaversineKm(requestLat!.Value, requestLng!.Value, c.RiderLat.Value, c.RiderLng.Value);
            })
            .First();
    }

    /// <summary>Haversine great-circle distance in kilometres.</summary>
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0; // Earth mean radius km

        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
