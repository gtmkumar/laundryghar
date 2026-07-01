namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>A RaaS booking a partner places against a brand's rider fleet (logistics.partner_bookings).
/// <see cref="PartnerId"/> is THE <c>rls_partner</c> isolation key. Mirrors the <see cref="RiderAssignment"/>
/// audit shape; pickup/drop are kept as minimal jsonb snapshots (no separate address entity for the MVP).
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at.</summary>
public class PartnerBooking
{
    public Guid Id { get; set; }

    /// <summary>FK to logistics.partners — THE <c>rls_partner</c> isolation key.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>Optional brand whose rider fleet serves this booking. No FK — brands are an
    /// admin-only, cross-schema (tenancy_org) tenant registry; the column is a soft reference only.</summary>
    public Guid? BrandId { get; set; }

    /// <summary>FK to logistics.partner_users — the partner user who raised the booking.</summary>
    public Guid CreatedByPartnerUserId { get; set; }

    /// <summary>Minimal pickup snapshot (address / contact / geo) captured as jsonb.</summary>
    public string PickupSnapshot { get; set; } = null!;

    /// <summary>Minimal drop snapshot (address / contact / geo) captured as jsonb.</summary>
    public string DropSnapshot { get; set; } = null!;

    public decimal? QuotedFare { get; set; }

    /// <summary>requested|assigned|in_progress|completed|cancelled (default 'requested').
    /// See <c>logistics.partner_bookings</c> CHECK constraint.</summary>
    public string Status { get; set; } = "requested";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Partner Partner { get; set; } = null!;
    public PartnerUser CreatedByPartnerUser { get; set; } = null!;
}
