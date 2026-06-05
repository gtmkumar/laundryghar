namespace laundryghar.Warehouse.Infrastructure.Auth;
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string PermissionCode { get; }
    public PermissionRequirement(string code) => PermissionCode = code;
}
