namespace laundryghar.SharedDataModel.Entities.Analytics;

/// <summary>
/// Read-only projection of analytics.mv_warehouse_throughput.
/// Keyless entity — never tracked for writes.
/// </summary>
public sealed class WarehouseThroughput
{
    public Guid BrandId             { get; set; }
    public Guid WarehouseId         { get; set; }
    public DateOnly ThroughputDate  { get; set; }
    public long GarmentsReceived    { get; set; }
    public long GarmentsDelivered   { get; set; }
    public long IssuesCount         { get; set; }
    public long RewashCount         { get; set; }

    /// <summary>Nullable: avg_tat_hours is NULL when no garments have completed a turnaround
    /// yet (received but not delivered) — AVG over zero rows yields NULL in the MV.</summary>
    public decimal? AvgTatHours     { get; set; }
}
