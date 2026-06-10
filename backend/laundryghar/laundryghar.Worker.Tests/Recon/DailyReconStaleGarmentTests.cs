using laundryghar.SharedDataModel.Enums;
using laundryghar.Worker.Options;

namespace laundryghar.Worker.Tests.Recon;

/// <summary>
/// Unit tests for the stale-garment selection logic used by DailyReconService.
///
/// The logic under test is:
///   A garment is a "stale candidate" if:
///     (a) its status is "active", AND
///     (b) its currentStage is not a terminal stage (dispatched/delivered/returned/lost/damaged), AND
///     (c) its lastScannedAt is null OR older than (now - ReconStaleHours).
///
/// These tests exercise the selection predicate in isolation (pure in-memory).
/// No database or DI required.
/// </summary>
public sealed class DailyReconStaleGarmentTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static readonly string[] TerminalStages =
        ["dispatched", "delivered", "returned", "lost", "damaged"];

    private static readonly string[] InFlightStages =
        [GarmentStage.Received, GarmentStage.Sorting, GarmentStage.Washing,
         GarmentStage.Drying, GarmentStage.Ironing, GarmentStage.Qc,
         GarmentStage.Packing];

    /// <summary>Replicates the staleness predicate from DailyReconService.ProcessWarehouseAsync.</summary>
    private static bool IsStaleCandidate(
        string status,
        string currentStage,
        DateTimeOffset? lastScannedAt,
        DateTimeOffset now,
        int staleHours)
    {
        var staleAt = now.AddHours(-staleHours);

        return status == "active"
            && !TerminalStages.Contains(currentStage)
            && (lastScannedAt == null || lastScannedAt <= staleAt);
    }

    // ── Happy path ───────────────────────────────────────────────────────────────

    [Fact]
    public void StaleCandidate_NullLastScannedAt_IsIncluded()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(IsStaleCandidate("active", GarmentStage.Washing, null, now, 12));
    }

    [Fact]
    public void StaleCandidate_ScanOlderThanThreshold_IsIncluded()
    {
        var now        = DateTimeOffset.UtcNow;
        var lastScan   = now.AddHours(-13); // 13h ago, threshold = 12h
        Assert.True(IsStaleCandidate("active", GarmentStage.Ironing, lastScan, now, 12));
    }

    [Fact]
    public void StaleCandidate_ScanExactlyAtThreshold_IsIncluded()
    {
        var now      = DateTimeOffset.UtcNow;
        var lastScan = now.AddHours(-12); // exactly at boundary → <=
        Assert.True(IsStaleCandidate("active", GarmentStage.Sorting, lastScan, now, 12));
    }

    // ── Exclusion cases ───────────────────────────────────────────────────────────

    [Fact]
    public void StaleCandidate_RecentlyScan_IsExcluded()
    {
        var now      = DateTimeOffset.UtcNow;
        var lastScan = now.AddHours(-6); // 6h ago, threshold = 12h
        Assert.False(IsStaleCandidate("active", GarmentStage.Washing, lastScan, now, 12));
    }

    [Theory]
    [InlineData("dispatched")]
    [InlineData("delivered")]
    [InlineData("returned")]
    [InlineData("lost")]
    [InlineData("damaged")]
    public void StaleCandidate_TerminalStage_IsExcluded(string stage)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(IsStaleCandidate("active", stage, null, now, 12));
    }

    [Fact]
    public void StaleCandidate_InactiveGarment_IsExcluded()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(IsStaleCandidate("inactive", GarmentStage.Washing, null, now, 12));
    }

    // ── All in-flight stages are eligible ─────────────────────────────────────────

    [Theory]
    [InlineData(GarmentStage.Received)]
    [InlineData(GarmentStage.Sorting)]
    [InlineData(GarmentStage.Washing)]
    [InlineData(GarmentStage.Drying)]
    [InlineData(GarmentStage.Ironing)]
    [InlineData(GarmentStage.Qc)]
    [InlineData(GarmentStage.Packing)]
    public void StaleCandidate_AllInFlightStages_AreEligibleWhenStale(string stage)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(IsStaleCandidate("active", stage, null, now, 12));
    }

    // ── Configurable stale threshold ─────────────────────────────────────────────

    [Fact]
    public void StaleCandidate_ShortThreshold_CatchesMoreGarments()
    {
        var now      = DateTimeOffset.UtcNow;
        var lastScan = now.AddHours(-3); // 3h ago
        // With 12h threshold: not stale.
        Assert.False(IsStaleCandidate("active", GarmentStage.Washing, lastScan, now, 12));
        // With 2h threshold: stale.
        Assert.True(IsStaleCandidate("active", GarmentStage.Washing, lastScan, now, 2));
    }

    // ── WorkerOptions default values ──────────────────────────────────────────────

    [Fact]
    public void WorkerOptions_DailyReconDefaults_AreCorrect()
    {
        var opts = new WorkerOptions();

        Assert.False(opts.DailyReconEnabled);
        Assert.Equal(21, opts.DailyReconHourLocal);
        Assert.Equal(12, opts.ReconStaleHours);
    }
}
