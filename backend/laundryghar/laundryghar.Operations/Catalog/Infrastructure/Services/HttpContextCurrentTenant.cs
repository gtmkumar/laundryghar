using laundryghar.SharedDataModel.Contracts;

namespace laundryghar.Catalog.Infrastructure.Services;

/// <summary>
/// ICurrentTenant backed by HttpContext JWT claims.
/// Populated by TenantResolutionMiddleware after authentication.
/// Platform admins may set BypassRls=true via middleware policy.
/// </summary>
public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentTenant(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? BrandId     => GetGuid("brand_id");
    public Guid? FranchiseId => GetGuid("franchise_id");
    public Guid? StoreId     => GetGuid("store_id");
    public Guid? UserId      => GetGuid(ClaimTypes.NameIdentifier);
    public bool  BypassRls   => _accessor.HttpContext?.Items["bypass_rls"] is true;

    private Guid? GetGuid(string claimType)
    {
        var value = _accessor.HttpContext?.User?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var g) ? g : null;
    }
}
