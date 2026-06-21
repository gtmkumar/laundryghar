namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>Per-user allow/deny on a single permission (identity_access.user_permission_override),
/// layered on top of role grants. Deny always wins. Lets you give a user a broad role with a
/// precise exception (or one extra permission) without minting a new role.</summary>
public class UserPermissionOverride
{
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public string Effect { get; set; } = "allow";  // 'allow' | 'deny'
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Permission Permission { get; set; } = null!;
}
