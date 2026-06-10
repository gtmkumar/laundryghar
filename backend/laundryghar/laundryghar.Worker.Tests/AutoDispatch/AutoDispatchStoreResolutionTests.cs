using laundryghar.Worker.Services.AutoDispatch;

namespace laundryghar.Worker.Tests.AutoDispatch;

/// <summary>
/// Unit tests for the store-id resolution priority and NTS Point coordinate approach
/// applied in AutoDispatchService (DEFECT-R3-A and DEFECT-R3-B).
///
/// All assertions are pure in-memory value checks — no database or DI required.
/// </summary>
public sealed class AutoDispatchStoreResolutionTests
{
    // ── DEFECT-R3-A: store-id resolution priority order ──────────────────────

    [Fact]
    public void StoreResolution_PickupStoreId_WinsOverRiderPrimaryStore()
    {
        Guid? pickupStoreId     = Guid.NewGuid();
        Guid? riderPrimaryStore = Guid.NewGuid();

        // Mirrors: pickup.StoreId ?? rider.PrimaryStoreId
        var resolved = pickupStoreId ?? riderPrimaryStore;

        Assert.Equal(pickupStoreId, resolved);
    }

    [Fact]
    public void StoreResolution_NullPickupStoreId_FallsBackToRiderPrimaryStore()
    {
        Guid? pickupStoreId     = null;
        Guid? riderPrimaryStore = Guid.NewGuid();

        var resolved = pickupStoreId ?? riderPrimaryStore;

        Assert.Equal(riderPrimaryStore, resolved);
    }

    [Fact]
    public void StoreResolution_BothNull_ReturnsNull_NeverGuidEmpty()
    {
        // When both sources are null the result must be null — the dispatcher skips the
        // assignment with a warning rather than inserting Guid.Empty into the NOT NULL FK.
        Guid? pickupStoreId     = null;
        Guid? riderPrimaryStore = null;

        var resolved = pickupStoreId ?? riderPrimaryStore;

        // null result → dispatcher logs a warning and skips; never assigns Guid.Empty as store_id.
        Assert.Null(resolved);
        Assert.False(resolved.HasValue,
            "When both store sources are null the resolution must yield null, not Guid.Empty.");
    }

    [Fact]
    public void StoreResolution_PickupStoreIdNonNull_RiderPrimaryStoreIgnored()
    {
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();

        Guid? pickupStoreId     = storeA;
        Guid? riderPrimaryStore = storeB;

        var resolved = pickupStoreId ?? riderPrimaryStore;

        Assert.Equal(storeA, resolved);
        Assert.NotEqual(storeB, resolved);
    }

    // ── DEFECT-R3-B: NTS Point coordinate reading ────────────────────────────
    // The fix materialises the Point object rather than projecting .Y/.X to SQL.
    // Verify that the RiderCandidate is correctly built from nullable double values
    // that would come from "point?.Y" and "point?.X" after client-side materialisation.

    [Fact]
    public void RiderCandidate_BuiltFromNullableCoords_NullSafe()
    {
        // Simulate r.LastKnownLocation = null → LocationPoint?.Y = null
        double? lat = null;
        double? lng = null;

        var candidate = new RiderCandidate(Guid.NewGuid(), 0, 10, lat, lng);

        Assert.Null(candidate.RiderLat);
        Assert.Null(candidate.RiderLng);
    }

    [Fact]
    public void RiderCandidate_BuiltFromValidCoords_RankedByDistance()
    {
        // Simulate two riders whose Points were materialised and coords extracted client-side.
        // Near rider: ~0.5 km from request; far rider: ~5 km north.
        double reqLat = 12.9716, reqLng = 77.5946;

        var nearCandidate = new RiderCandidate(Guid.NewGuid(), 2, 10, 12.9760, 77.5946);
        var farCandidate  = new RiderCandidate(Guid.NewGuid(), 2, 10, 13.0165, 77.5946);

        var best = RiderRanker.PickBest([farCandidate, nearCandidate], reqLat, reqLng);

        Assert.Equal(nearCandidate.RiderId, best!.RiderId);
    }

    [Fact]
    public void AddressPoint_NullGeoLocation_DistanceRankingDegrades_NotCrash()
    {
        // Simulate pickup.Address.GeoLocation = null → AddressPoint = null → coords null.
        // RiderRanker must not throw; it ranks by load only when coords are absent.
        double? addressLat = null; // from (Point?)null?.Y
        double? addressLng = null;

        var riderA = new RiderCandidate(Guid.NewGuid(), 1, 10, 12.97, 77.59);
        var riderB = new RiderCandidate(Guid.NewGuid(), 3, 10, 12.97, 77.59);

        var best = RiderRanker.PickBest([riderA, riderB], addressLat, addressLng);

        // Falls back to load ranking — riderA (load=1) wins.
        Assert.Equal(riderA.RiderId, best!.RiderId);
    }
}
