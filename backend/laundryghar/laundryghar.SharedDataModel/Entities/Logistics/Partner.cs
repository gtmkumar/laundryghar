using laundryghar.SharedDataModel.Common;

namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>Rider-as-a-Service (RaaS) partner organisation — the tenant isolation ROOT for the
/// RaaS MVP (logistics.partners). The <c>partner_id</c> that <c>rls_partner</c> filters on equals
/// this row's <see cref="Id"/>.
/// Has created_at, updated_at, created_by, updated_by (audit) — no version column (not IAuditableEntity).
/// Has deleted_at (ISoftDeletable), mirroring <see cref="Rider"/>.</summary>
public class Partner : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Short, human-friendly unique partner handle (onboarding / support reference).</summary>
    public string Code { get; set; } = null!;

    public string LegalName { get; set; } = null!;

    /// <summary>active|suspended|terminated. See <c>logistics.partners</c> CHECK constraint.</summary>
    public string Status { get; set; } = null!;

    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigations
    public ICollection<PartnerUser> Users { get; set; } = [];
    public ICollection<PartnerBooking> Bookings { get; set; } = [];
}
