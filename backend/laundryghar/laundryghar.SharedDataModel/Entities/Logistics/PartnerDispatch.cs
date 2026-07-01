namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>
/// Rider dispatch for a RaaS partner booking (logistics.partner_dispatches).
///
/// SEPARATE from the hot, partitioned <c>order_lifecycle.delivery_assignments</c> table by design
/// (FULL-11b / issue #14): the partner-booking rider lifecycle lives in its own logistics table so
/// the order fulfilment spine stays untouched. The track/OTP/proof/last-known-location fields below
/// are REPLICATED from <see cref="OrderLifecycle.DeliveryAssignment"/> — deliberately, not referenced.
///
/// DUAL VISIBILITY: this row is scoped by the combined <c>rls_partner_or_brand</c> policy so it is
/// visible to BOTH the owning <see cref="PartnerId"/> (a partner session, to TRACK) AND the serving
/// LaundryGhar <see cref="BrandId"/> fleet's staff (a brand session, to DISPATCH/manage). Hence it
/// carries both keys. See db/patches/rls_partner_dispatch.sql.
///
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at (mirrors
/// <see cref="OrderLifecycle.DeliveryAssignment"/>).
/// </summary>
public class PartnerDispatch
{
    public Guid Id { get; set; }

    /// <summary>Owning partner — one arm of the <c>rls_partner_or_brand</c> policy. Copied from the
    /// booking so a partner session (partner_id set, no brand) can track its own dispatch.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>FK → logistics.partner_bookings(id). The booking this dispatch fulfils.</summary>
    public Guid PartnerBookingId { get; set; }

    /// <summary>Serving brand whose rider fleet fulfils the booking — the OTHER arm of the
    /// <c>rls_partner_or_brand</c> policy, so a brand-staff session (brand_id set, no partner) can
    /// dispatch/manage it. Nullable: a dispatch may exist before a serving brand is set (the RLS
    /// brand arm is guarded by <c>brand_id IS NOT NULL</c>). Scalar reference only — brands are an
    /// admin-only, cross-schema tenant registry, so NO FK (mirrors partner_bookings.brand_id).</summary>
    public Guid? BrandId { get; set; }

    /// <summary>Assigned rider — nullable until a rider is picked. Scalar cross-reference to
    /// logistics.riders (no FK: a dispatch may be created 'pending' with no rider yet).</summary>
    public Guid? RiderId { get; set; }

    /// <summary>pending|assigned|en_route_pickup|picked_up|en_route_drop|delivered|cancelled
    /// (default 'pending'). See the partner_dispatches status CHECK constraint.</summary>
    public string Status { get; set; } = "pending";

    // ── Verification (replicated from DeliveryAssignment OTP flow) ────────────
    /// <summary>Pickup verification code shown to the sender; the rider enters it to confirm pickup.</summary>
    public string? PickupOtp { get; set; }
    /// <summary>Drop verification code shown to the recipient; the rider enters it to confirm drop.</summary>
    public string? DropOtp { get; set; }
    public DateTimeOffset? PickupVerifiedAt { get; set; }
    public DateTimeOffset? DropVerifiedAt { get; set; }

    // ── Proof of delivery (replicated from DeliveryAssignment) ────────────────
    public string? ProofPhotoUrl { get; set; }
    public string? ProofSignatureUrl { get; set; }

    // ── Last-known location (track — replicated concept from rider location pings) ──
    public decimal? LastKnownLat { get; set; }
    public decimal? LastKnownLng { get; set; }
    public DateTimeOffset? LastLocationAt { get; set; }

    /// <summary>When a rider was assigned (Status → 'assigned').</summary>
    public DateTimeOffset? AssignedAt { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
