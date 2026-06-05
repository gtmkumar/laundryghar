namespace laundryghar.SharedDataModel.Entities.Analytics;

/// <summary>
/// Read-only projection of analytics.mv_rider_performance.
/// Keyless entity — never tracked for writes.
/// </summary>
public sealed class RiderPerformance
{
    public Guid BrandId               { get; set; }
    public Guid FranchiseId           { get; set; }
    public Guid RiderId               { get; set; }
    public string RiderCode           { get; set; } = null!;
    public DateOnly PerfDate          { get; set; }
    public long AssignmentsTotal      { get; set; }
    public long AssignmentsCompleted  { get; set; }
    public long AssignmentsFailed     { get; set; }
    public long PickupsDone           { get; set; }
    public long DeliveriesDone        { get; set; }
    public decimal TotalKm            { get; set; }
    public decimal AvgDurationMin     { get; set; }
    public decimal RatingAverage      { get; set; }
    public decimal CompletionRate     { get; set; }
}
