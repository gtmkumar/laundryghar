using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>RBAC role, optionally scoped to a brand (identity_access.roles).
/// Has created_at, updated_at, created_by, updated_by, deleted_at, status.
/// No version — does NOT implement IAuditableEntity.</summary>
public class Role
{
    public Guid Id { get; set; }
    public Guid? BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string ScopeType { get; set; } = null!;

    /// <summary>The industry vertical this role belongs to (laundry/salon/logistics), or
    /// <c>null</c> for a vertical-neutral role visible to every brand. Laundry-specific system
    /// roles (warehouse_*) are tagged 'laundry'; salon/logistics get their own on-site roles.
    /// Gated the same way modules are — see <see cref="Enums.VerticalKey.IsAvailableTo"/>.</summary>
    public string? VerticalKey { get; set; }

    public bool IsSystem { get; set; }
    public bool IsAssignable { get; set; }
    public short Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand? Brand { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
    public ICollection<UserScopeMembership> UserScopeMemberships { get; set; } = [];
}
