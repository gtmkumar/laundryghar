namespace laundryghar.Commerce.Infrastructure.Auth;

/// <summary>Authorization requirement carrying a single permission code.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string PermissionCode { get; }
    public PermissionRequirement(string permissionCode) => PermissionCode = permissionCode;
}
