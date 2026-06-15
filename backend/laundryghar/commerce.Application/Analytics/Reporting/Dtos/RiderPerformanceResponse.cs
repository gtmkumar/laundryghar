using laundryghar.SharedDataModel.Entities.Analytics;

namespace commerce.Application.Analytics.Reporting.Dtos;

/// <summary>
/// API projection of <see cref="RiderPerformance"/>. The four aggregate columns
/// (total_km, avg_duration_min, rating_average, completion_rate) are NULL in the
/// matview when a rider has no qualifying rows for the day; they are coalesced to 0
/// here so the JSON contract remains numeric (no nulls) for existing clients.
/// </summary>
public sealed record RiderPerformanceResponse(
    Guid BrandId,
    Guid FranchiseId,
    Guid RiderId,
    string RiderCode,
    DateOnly PerfDate,
    long AssignmentsTotal,
    long AssignmentsCompleted,
    long AssignmentsFailed,
    long PickupsDone,
    long DeliveriesDone,
    decimal TotalKm,
    decimal AvgDurationMin,
    decimal RatingAverage,
    decimal CompletionRate)
{
    public static RiderPerformanceResponse From(RiderPerformance e) => new(
        e.BrandId, e.FranchiseId, e.RiderId, e.RiderCode, e.PerfDate,
        e.AssignmentsTotal, e.AssignmentsCompleted, e.AssignmentsFailed,
        e.PickupsDone, e.DeliveriesDone,
        e.TotalKm        ?? 0m,
        e.AvgDurationMin ?? 0m,
        e.RatingAverage  ?? 0m,
        e.CompletionRate ?? 0m);
}
