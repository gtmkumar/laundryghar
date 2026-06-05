using laundryghar.SharedDataModel.Contracts;

namespace laundryghar.Orders.Infrastructure.Services;

public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentTenant(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? BrandId     => GetGuid("brand_id");
    public Guid? FranchiseId => GetGuid("franchise_id");
    public Guid? StoreId     => GetGuid("store_id");
    public Guid? UserId      => GetGuid(ClaimTypes.NameIdentifier);
    public bool  BypassRls   => _accessor.HttpContext?.Items["bypass_rls"] is true;

    private Guid? GetGuid(string t)
    {
        var v = _accessor.HttpContext?.User?.FindFirstValue(t);
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
