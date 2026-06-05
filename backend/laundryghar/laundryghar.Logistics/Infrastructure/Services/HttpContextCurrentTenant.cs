using laundryghar.SharedDataModel.Contracts;

namespace laundryghar.Logistics.Infrastructure.Services;

public sealed class HttpContextCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _a;
    public HttpContextCurrentTenant(IHttpContextAccessor a) => _a = a;

    public Guid? BrandId     => G("brand_id");
    public Guid? FranchiseId => G("franchise_id");
    public Guid? StoreId     => G("store_id");
    public Guid? UserId      => G(ClaimTypes.NameIdentifier);
    public bool  BypassRls   => _a.HttpContext?.Items["bypass_rls"] is true;

    private Guid? G(string t) =>
        Guid.TryParse(_a.HttpContext?.User?.FindFirstValue(t), out var g) ? g : null;
}
