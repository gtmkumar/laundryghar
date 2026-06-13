namespace laundryghar.Commerce.Infrastructure.Services;

/// <summary>UUID-based current-user context for the Commerce service.</summary>
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

    /// <summary>
    /// Effective brand for write operations.
    /// - Brand-scoped tokens: returns BrandId from JWT claim.
    /// - Platform admins: returns X-Brand-Id override if present; else BrandId.
    /// Throws UnauthorizedAccessException if no brand can be resolved.
    /// </summary>
    Guid RequireBrandId();
}
