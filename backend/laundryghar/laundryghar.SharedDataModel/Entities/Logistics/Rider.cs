using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>Delivery rider registered under a franchise (logistics.riders).
/// Has created_at, updated_at, created_by, updated_by — no version column (not IAuditableEntity).
/// Has deleted_at (ISoftDeletable).
/// last_known_location is GEOGRAPHY(Point,4326) — nullable.</summary>
public class Rider : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>FK to identity_access.users — the rider's user account.</summary>
    public Guid UserId { get; set; }

    public Guid BrandId { get; set; }
    public Guid FranchiseId { get; set; }

    /// <summary>FK to tenancy_org.stores — optional primary store assignment.</summary>
    public Guid? PrimaryStoreId { get; set; }

    public string RiderCode { get; set; } = null!;
    public string EmploymentType { get; set; } = null!;
    public string? AadhaarNumberMasked { get; set; }
    public string? PanNumber { get; set; }
    public string? DrivingLicenseNumber { get; set; }
    public DateOnly? DlExpiryDate { get; set; }
    public string VehicleType { get; set; } = null!;
    public string? VehicleNumber { get; set; }
    public string? VehicleModel { get; set; }
    public DateOnly? InsuranceExpiryDate { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankAccountName { get; set; }
    public string? UpiId { get; set; }
    public int DailyPickupCapacity { get; set; }
    public int DailyDeliveryCapacity { get; set; }
    public decimal ServiceRadiusKm { get; set; }
    public decimal? RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public decimal? CompletionRate { get; set; }
    public int LifetimeDeliveries { get; set; }

    /// <summary>GEOGRAPHY(Point,4326) — nullable; updated by location ping.</summary>
    public Point? LastKnownLocation { get; set; }

    public DateTimeOffset? LastPingAt { get; set; }
    public bool IsOnline { get; set; }
    public bool IsOnDuty { get; set; }
    public DateTimeOffset? OnDutySince { get; set; }
    public int CurrentLoad { get; set; }
    public string KycStatus { get; set; } = null!;
    public DateTimeOffset? KycVerifiedAt { get; set; }

    /// <summary>Vehicle review gate — pending|under_review|approved|rejected.
    /// Dispatch requires 'approved' (combined with kyc verified). See <see cref="Enums.VehicleVerificationStatus"/>.</summary>
    public string VehicleVerificationStatus { get; set; } = "pending";
    public DateTimeOffset? VehicleVerifiedAt { get; set; }
    public Guid? VehicleVerifiedBy { get; set; }
    public string? VehicleRejectionReason { get; set; }

    public DateTimeOffset? OnboardedAt { get; set; }
    public string Status { get; set; } = null!;
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public User User { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Franchise Franchise { get; set; } = null!;
    public Store? PrimaryStore { get; set; }
    public ICollection<RiderAssignment> Assignments { get; set; } = [];
    public ICollection<RiderCapacityConfig> CapacityConfigs { get; set; } = [];
    public ICollection<RiderLocationPing> LocationPings { get; set; } = [];
    public ICollection<RiderDocument> Documents { get; set; } = [];
}
