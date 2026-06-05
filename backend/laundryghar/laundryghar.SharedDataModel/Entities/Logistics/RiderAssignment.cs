using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>Scheduled or completed shift assignment for a rider at a store (logistics.rider_assignments).
/// Has created_at, updated_at, created_by — no updated_by, no version, no deleted_at.
/// UNIQUE on (rider_id, shift_date, shift_start).</summary>
public class RiderAssignment
{
    public Guid Id { get; set; }
    public Guid RiderId { get; set; }
    public Guid BrandId { get; set; }
    public Guid StoreId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public TimeOnly ShiftStart { get; set; }
    public TimeOnly ShiftEnd { get; set; }
    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? ActualEndAt { get; set; }
    public int MaxPickups { get; set; }
    public int MaxDeliveries { get; set; }
    public int CompletedPickups { get; set; }
    public int CompletedDeliveries { get; set; }
    public int FailedAttempts { get; set; }
    public decimal? TotalDistanceKm { get; set; }
    public decimal? Earnings { get; set; }
    public string Status { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Rider Rider { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public ICollection<RiderLocationPing> LocationPings { get; set; } = [];
}
