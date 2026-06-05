namespace laundryghar.Identity.Infrastructure.Services;

/// <summary>UUID-based current-user context for the Identity service. Replaces Utilities' int-ID ICurrentUserService.</summary>
public interface ICurrentUser
{
    Guid? UserId      { get; }
    string? UserType  { get; }
    string? Email     { get; }
    string? Phone     { get; }
    Guid? BrandId     { get; }
    Guid? FranchiseId { get; }
    Guid? StoreId     { get; }
    string? ScopeType { get; }
    Guid? ScopeId     { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    bool HasPermission(string permissionCode);
}
