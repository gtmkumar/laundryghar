namespace laundryghar.SharedDataModel.Entities.Logistics;

/// <summary>Login principal for a RaaS partner (logistics.partner_users). The row <see cref="Id"/>
/// is the JWT <c>sub</c> for partner-scoped sessions; <see cref="PartnerId"/> is carried in the
/// <c>partner_id</c> claim and drives <c>rls_partner</c>.
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at.</summary>
public class PartnerUser
{
    /// <summary>PK — also the JWT subject (<c>sub</c>) for partner sessions.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to logistics.partners — the RLS isolation key.</summary>
    public Guid PartnerId { get; set; }

    public string? PhoneE164 { get; set; }
    public string? Email { get; set; }

    /// <summary>partner_admin|partner_operator. See <c>logistics.partner_users</c> CHECK constraint.</summary>
    public string PartnerRole { get; set; } = null!;

    /// <summary>active|suspended|invited. See <c>logistics.partner_users</c> CHECK constraint.</summary>
    public string Status { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Partner Partner { get; set; } = null!;
}
