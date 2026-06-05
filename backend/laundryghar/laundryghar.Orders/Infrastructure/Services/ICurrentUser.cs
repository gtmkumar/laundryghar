namespace laundryghar.Orders.Infrastructure.Services;

public interface ICurrentUser
{
    Guid? UserId      { get; }
    string? UserType  { get; }
    Guid? BrandId     { get; }
    Guid? FranchiseId { get; }
    Guid? StoreId     { get; }
    string? ScopeType { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    bool HasPermission(string code);

    /// <summary>
    /// Returns X-Brand-Id override for platform_admin, else JWT brand_id claim.
    /// Throws UnauthorizedAccessException if no brand can be resolved.
    /// </summary>
    Guid RequireBrandId();
}
