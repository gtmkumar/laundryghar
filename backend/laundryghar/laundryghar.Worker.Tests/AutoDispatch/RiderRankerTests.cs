using laundryghar.Worker.Services.AutoDispatch;

// RiderRanker is a pure static class in the laundryghar.Worker assembly (ProjectReference).

namespace laundryghar.Worker.Tests.AutoDispatch;

/// <summary>
/// Unit tests for <see cref="RiderRanker.PickBest"/>.
/// No database, no DI — all assertions are in-memory value checks.
/// </summary>
public sealed class RiderRankerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static RiderCandidate Make(
        int     load       = 0,
        int     capacity   = 10,
        double? lat        = null,
        double? lng        = null) =>
        new(Guid.NewGuid(), load, capacity, lat, lng);

    // ── empty / single ────────────────────────────────────────────────────────

    [Fact]
    public void PickBest_EmptyList_ReturnsNull()
    {
        var result = RiderRanker.PickBest([], requestLat: null, requestLng: null);
        Assert.Null(result);
    }

    [Fact]
    public void PickBest_SingleCandidate_ReturnsThat()
    {
        var only = Make(load: 2);
        var result = RiderRanker.PickBest([only], null, null);
        Assert.Equal(only.RiderId, result!.RiderId);
    }

    // ── load-first ordering ───────────────────────────────────────────────────

    [Fact]
    public void PickBest_PrefersLowerLoad_WhenNoCoords()
    {
        var busy = Make(load: 5);
        var idle = Make(load: 1);
        var also = Make(load: 3);

        var result = RiderRanker.PickBest([busy, also, idle], null, null);
        Assert.Equal(idle.RiderId, result!.RiderId);
    }

    [Fact]
    public void PickBest_PrefersLowerLoad_EvenWhenCoordsPresent()
    {
        // rider A: load=0, far away; rider B: load=5, very close.
        // Load wins over distance.
        var riderA = Make(load: 0, lat: 0.0, lng: 0.0);
        var riderB = Make(load: 5, lat: 12.9716, lng: 77.5946); // Bangalore, close to request

        // Request is at Bangalore coords
        var result = RiderRanker.PickBest([riderA, riderB], requestLat: 12.9716, requestLng: 77.5946);
        Assert.Equal(riderA.RiderId, result!.RiderId);
    }

    // ── distance tiebreaking ──────────────────────────────────────────────────

    [Fact]
    public void PickBest_SameLoad_PrefersCloserRider()
    {
        // Both at load=2; rider A is 5 km away, rider B is 1 km away.
        // Request at (12.97, 77.59)
        double reqLat = 12.9716, reqLng = 77.5946;

        // Far rider: ~5 km north
        var farRider  = Make(load: 2, lat: 13.0165, lng: 77.5946);
        // Close rider: ~0.5 km away
        var nearRider = Make(load: 2, lat: 12.9760, lng: 77.5946);

        var result = RiderRanker.PickBest([farRider, nearRider], reqLat, reqLng);
        Assert.Equal(nearRider.RiderId, result!.RiderId);
    }

    [Fact]
    public void PickBest_SameLoad_RiderWithoutCoordsRanksAfterRiderWithCoords()
    {
        double reqLat = 12.9716, reqLng = 77.5946;

        var withCoords    = Make(load: 3, lat: 12.9760, lng: 77.5946);
        var withoutCoords = Make(load: 3, lat: null,   lng: null);

        var result = RiderRanker.PickBest([withoutCoords, withCoords], reqLat, reqLng);
        Assert.Equal(withCoords.RiderId, result!.RiderId);
    }

    [Fact]
    public void PickBest_NoRequestCoords_BothRidersHaveCoords_FallsBackToLoad()
    {
        // No request coords: distance is indeterminate → rank purely by load.
        var heavy = Make(load: 4, lat: 12.97, lng: 77.59);
        var light = Make(load: 1, lat: 13.00, lng: 77.59);

        var result = RiderRanker.PickBest([heavy, light], requestLat: null, requestLng: null);
        Assert.Equal(light.RiderId, result!.RiderId);
    }

    // ── haversine sanity ──────────────────────────────────────────────────────

    [Fact]
    public void HaversineKm_KnownDistance_IsApproximatelyCorrect()
    {
        // London (51.5074, -0.1278) to Paris (48.8566, 2.3522) ≈ 340 km.
        double dist = RiderRanker.HaversineKm(51.5074, -0.1278, 48.8566, 2.3522);
        Assert.InRange(dist, 330.0, 350.0);
    }

    [Fact]
    public void HaversineKm_SamePoint_IsZero()
    {
        double dist = RiderRanker.HaversineKm(12.9716, 77.5946, 12.9716, 77.5946);
        Assert.Equal(0.0, dist, precision: 5);
    }

    // ── capacity guard (consumer responsibility) ──────────────────────────────

    [Fact]
    public void PickBest_AllCandidatesAtCapacity_StillPicksBest()
    {
        // RiderRanker does NOT enforce capacity — that is the caller's job.
        // Confirm it returns the least-loaded even when all are "at capacity".
        var a = Make(load: 5, capacity: 5);
        var b = Make(load: 3, capacity: 3);

        var result = RiderRanker.PickBest([a, b], null, null);
        // b has lower absolute load — ranked first
        Assert.Equal(b.RiderId, result!.RiderId);
    }

    // ── determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void PickBest_TiedOnLoadAndDistance_IsStable()
    {
        // Both riders have load=2, no coords — result should be consistent.
        var c1 = Make(load: 2);
        var c2 = Make(load: 2);

        var result1 = RiderRanker.PickBest([c1, c2], null, null);
        var result2 = RiderRanker.PickBest([c1, c2], null, null);

        // Same input order → same winner (LINQ OrderBy is stable).
        Assert.Equal(result1!.RiderId, result2!.RiderId);
    }
}
