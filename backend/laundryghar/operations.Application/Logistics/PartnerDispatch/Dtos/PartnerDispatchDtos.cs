namespace operations.Application.Logistics.PartnerDispatch.Dtos;

/// <summary>Full dispatch projection (staff/fleet view). Returned by the assign/update commands.</summary>
public sealed record PartnerDispatchDto(
    Guid Id,
    Guid PartnerId,
    Guid PartnerBookingId,
    Guid? BrandId,
    Guid? RiderId,
    string Status,
    DateTimeOffset? PickupVerifiedAt,
    DateTimeOffset? DropVerifiedAt,
    string? ProofPhotoUrl,
    string? ProofSignatureUrl,
    decimal? LastKnownLat,
    decimal? LastKnownLng,
    DateTimeOffset? LastLocationAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <summary>Partner-facing track projection for a booking: the dispatch status + rider + last-known
/// location + verification/proof state. <see cref="Dispatched"/> is false when no dispatch exists yet
/// (the booking has been created but the serving fleet has not assigned a rider) — every dispatch
/// field is then null and <see cref="Status"/> is "not_dispatched".</summary>
public sealed record PartnerBookingTrackDto(
    Guid PartnerBookingId,
    bool Dispatched,
    Guid? DispatchId,
    string Status,
    Guid? RiderId,
    decimal? LastKnownLat,
    decimal? LastKnownLng,
    DateTimeOffset? LastLocationAt,
    DateTimeOffset? PickupVerifiedAt,
    DateTimeOffset? DropVerifiedAt,
    string? ProofPhotoUrl,
    string? ProofSignatureUrl,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? UpdatedAt
);

/// <summary>Staff creates/assigns a dispatch for a partner booking.
/// <para><see cref="PartnerId"/> is supplied by the caller (it is displayed on the incoming-booking
/// card) but is NOT trusted: the handler performs a controlled RLS-bypass read of the booking and
/// verifies the supplied value matches the booking's owning partner (rejecting a mismatch), deriving
/// the dispatch's partner arm from server state. brand_id is taken from the staff's brand context and
/// is verified against — or claimed onto — the booking's serving brand under the same bypass.</para></summary>
/// <param name="PartnerBookingId">The booking to fulfil (FK-checked; read under bypass for attribution).</param>
/// <param name="PartnerId">Must equal the booking's owning partner — verified server-side.</param>
/// <param name="RiderId">The rider to assign (optional; a dispatch may be created 'pending').</param>
/// <param name="PickupOtp">Optional pickup verification code.</param>
/// <param name="DropOtp">Optional drop verification code.</param>
public sealed record AssignPartnerDispatchRequest(
    Guid PartnerBookingId,
    Guid PartnerId,
    Guid? RiderId = null,
    string? PickupOtp = null,
    string? DropOtp = null
);

/// <summary>Staff advances a dispatch: change status, verify pickup/drop OTP, attach proof, and/or
/// push the rider's last-known location. Every field is optional — a call may update just one facet
/// (e.g. only the location ping). <see cref="Status"/>, when present, must be a valid transition.</summary>
public sealed record UpdatePartnerDispatchStatusRequest(
    string? Status = null,
    Guid? RiderId = null,
    bool VerifyPickupOtp = false,
    string? PickupOtp = null,
    bool VerifyDropOtp = false,
    string? DropOtp = null,
    string? ProofPhotoUrl = null,
    string? ProofSignatureUrl = null,
    double? LastKnownLat = null,
    double? LastKnownLng = null
);
