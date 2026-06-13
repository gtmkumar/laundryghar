namespace laundryghar.Analytics.Infrastructure.Auth;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string PermissionCode { get; }
    public PermissionRequirement(string code) => PermissionCode = code;
}
