namespace laundryghar.Warehouse.Infrastructure.Services;

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
}
