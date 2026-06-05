namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>Role ↔ Permission junction (identity_access.role_permissions).
/// Has id, role_id, permission_id, granted_at, granted_by, created_at, created_by.
/// No updated_at, updated_by, deleted_at, version.</summary>
public class RolePermission
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
