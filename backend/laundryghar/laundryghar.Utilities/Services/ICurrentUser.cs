namespace laundryghar.Utilities.Services;

/// <summary>UUID-based current-user context resolved from the request principal.
/// Cross-cutting: lives in Utilities so every bounded context can consume it.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserType { get; }
    string? Email { get; }
    string? Phone { get; }
    Guid? BrandId { get; }
    Guid? FranchiseId { get; }
    Guid? StoreId { get; }
    string? ScopeType { get; }
    Guid? ScopeId { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformAdmin { get; }
    bool HasPermission(string permissionCode);

    /// <summary>Effective brand for write operations. Platform admins: X-Brand-Id override
    /// (HttpContext item "brand_id_override") if present, else JWT brand_id.
    /// Throws <see cref="UnauthorizedAccessException"/> if no brand can be resolved.</summary>
    Guid RequireBrandId();
}
