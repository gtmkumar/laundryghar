namespace laundryghar.Engagement.Infrastructure.Services;

/// <summary>UUID-based current-user context for the Engagement service.</summary>
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
    /// Platform admins: returns X-Brand-Id override if present; else JWT brand_id.
    /// Throws UnauthorizedAccessException if no brand can be resolved.
    /// </summary>
    Guid RequireBrandId();
}
