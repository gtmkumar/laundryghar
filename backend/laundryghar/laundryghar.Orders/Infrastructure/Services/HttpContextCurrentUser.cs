namespace laundryghar.Orders.Infrastructure.Services;

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid?   UserId      => ParseGuid(ClaimTypes.NameIdentifier);
    public string? UserType    => Claim("user_type");
    public Guid?   BrandId     => ParseGuid("brand_id");
    public Guid?   FranchiseId => ParseGuid("franchise_id");
    public Guid?   StoreId     => ParseGuid("store_id");
    public string? ScopeType   => Claim("scope_type");
    public bool    IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public bool    IsPlatformAdmin =>
        UserType == laundryghar.SharedDataModel.Enums.UserType.PlatformAdmin
        || ScopeType == laundryghar.SharedDataModel.Enums.ScopeType.Platform;

    public bool HasPermission(string code)
    {
        var perms = Claim("permissions");
        if (string.IsNullOrEmpty(perms)) return false;
        return perms.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(code, StringComparer.OrdinalIgnoreCase);
    }

    public Guid RequireBrandId()
    {
        if (_accessor.HttpContext?.Items.TryGetValue("brand_id_override", out var v) == true
            && v is Guid g && g != Guid.Empty)
            return g;

        var b = BrandId;
        if (b.HasValue && b.Value != Guid.Empty) return b.Value;

        throw new UnauthorizedAccessException(
            "Brand context required. Platform admins must pass X-Brand-Id header.");
    }

    private string? Claim(string t) => Principal?.FindFirstValue(t);
    private Guid? ParseGuid(string t) =>
        Guid.TryParse(Claim(t), out var g) ? g : null;
}
