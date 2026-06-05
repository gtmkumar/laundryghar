namespace laundryghar.Warehouse.Infrastructure.Services;

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _a;
    public HttpContextCurrentUser(IHttpContextAccessor a) => _a = a;

    private ClaimsPrincipal? P => _a.HttpContext?.User;

    public Guid?   UserId      => ParseGuid(ClaimTypes.NameIdentifier);
    public string? UserType    => Claim("user_type");
    public Guid?   BrandId     => ParseGuid("brand_id");
    public Guid?   FranchiseId => ParseGuid("franchise_id");
    public Guid?   StoreId     => ParseGuid("store_id");

    public bool IsPlatformAdmin =>
        UserType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin
        || Claim("scope_type") == laundryghar.SharedDataModel.Enums.ScopeType.Platform;

    public Guid RequireBrandId()
    {
        if (_a.HttpContext?.Items.TryGetValue("brand_id_override", out var v) == true
            && v is Guid g && g != Guid.Empty) return g;
        var b = BrandId;
        if (b.HasValue && b.Value != Guid.Empty) return b.Value;
        throw new UnauthorizedAccessException(
            "Brand context required. Platform admins must pass X-Brand-Id header.");
    }

    private string? Claim(string t) => P?.FindFirstValue(t);
    private Guid? ParseGuid(string t) =>
        Guid.TryParse(Claim(t), out var g) ? g : null;
}
