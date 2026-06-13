using laundryghar.SharedDataModel.Entities.TenancyOrg;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Rider delivery/pickup assignment (order_lifecycle.delivery_assignments).
/// FK to orders uses composite key — scalar-only (order_id + order_created_at, no nav).
/// Has created_at, updated_at, created_by, updated_by. No version, no deleted_at.</summary>
public class DeliveryAssignment
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid StoreId { get; set; }

    /// <summary>FK to logistics.riders — cross-BC, scalar only.</summary>
    public Guid RiderId { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column for composite FK to orders — scalar only (no composite nav on nullable FK).</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public Guid? PickupRequestId { get; set; }
    public string LegType { get; set; } = null!;
    public short? SequenceNumber { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public Guid? AssignedBy { get; set; }

    /// <summary>offer_accept mode: when this job was offered to the rider.</summary>
    public DateTimeOffset? OfferedAt { get; set; }

    /// <summary>offer_accept mode: when an un-accepted offer lapses and is re-offered.</summary>
    public DateTimeOffset? OfferExpiresAt { get; set; }

    /// <summary>offer_accept mode: 1-based round number; re-offers increment this.</summary>
    public short? OfferRound { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }

    /// <summary>Pickup leg: items collected from the customer (set on pickup OTP verify).</summary>
    public DateTimeOffset? CollectedAt { get; set; }

    /// <summary>Pickup leg: items dropped at the store/laundry (set on store geofence).</summary>
    public DateTimeOffset? DroppedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public string AddressSnapshot { get; set; } = null!;

    /// <summary>GEOGRAPHY(Point,4326) — real geography type confirmed via \d+.</summary>
    public Point? GeoLocation { get; set; }

    public decimal? DistanceKm { get; set; }
    public int? DurationMinutes { get; set; }

    /// <summary>COD cash collected on this delivery leg (Phase 3). Null = prepaid / nothing due.</summary>
    public decimal? CodAmount { get; set; }
    public DateTimeOffset? CodCollectedAt { get; set; }

    /// <summary>logistics.rider_settlements.id that cleared this collection (scalar, cross-BC; null = outstanding).</summary>
    public Guid? SettlementId { get; set; }

    /// <summary>Rider earning for this leg (₹), computed from payout rates at completion (Phase 4).</summary>
    public decimal? PayoutAmount { get; set; }
    public bool OtpVerified { get; set; }
    public DateTimeOffset? OtpAttemptedAt { get; set; }
    public string? SignatureS3Key { get; set; }
    public string? ProofPhotoS3Key { get; set; }

    /// <summary>Timestamp when the rider uploaded the proof-of-delivery photo (null = no photo uploaded yet).</summary>
    public DateTimeOffset? ProofPhotoTakenAt { get; set; }

    public string? CustomerSignature { get; set; }
    public string Status { get; set; } = null!;
    public string? Notes { get; set; }
    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public PickupRequest? PickupRequest { get; set; }
}
