namespace laundryghar.Logistics.Infrastructure.Services;

public interface ICurrentUser
{
    Guid? UserId      { get; }
    string? UserType  { get; }
    Guid? BrandId     { get; }
    Guid? FranchiseId { get; }
    Guid? StoreId     { get; }
    bool IsPlatformAdmin { get; }
    /// <summary>Returns X-Brand-Id override for platform_admin, else JWT brand_id. Throws if unresolvable.</summary>
    Guid RequireBrandId();
    /// <summary>Returns true if the JWT permissions claim contains <paramref name="permissionCode"/>.</summary>
    bool HasPermission(string permissionCode);
}
