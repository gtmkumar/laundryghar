using System.Security.Claims;
using laundryghar.SharedDataModel.Enums;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Utilities.Services;

/// <summary><see cref="ICurrentUser"/> backed by HttpContext JWT claims.</summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId => ParseGuid(ClaimTypes.NameIdentifier);
    public string? UserType => Claim("user_type");
    public string? Email => Claim("email");
    public string? Phone => Claim("phone");
    public Guid? BrandId => ParseGuid("brand_id");
    public Guid? FranchiseId => ParseGuid("franchise_id");
    public Guid? StoreId => ParseGuid("store_id");
    public string? ScopeType => Claim("scope_type");
    public Guid? ScopeId => ParseGuid("scope_id");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsPlatformAdmin =>
        UserType == SharedDataModel.Enums.UserType.PlatformAdmin
        || ScopeType == SharedDataModel.Enums.ScopeType.Platform;

    public bool HasPermission(string permissionCode)
    {
        var perms = Claim("permissions");
        if (string.IsNullOrEmpty(perms)) return false;
        return perms.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public Guid RequireBrandId()
    {
        if (_accessor.HttpContext?.Items.TryGetValue("brand_id_override", out var overrideVal) == true
            && overrideVal is Guid overrideGuid && overrideGuid != Guid.Empty)
            return overrideGuid;

        var claimBrand = BrandId;
        if (claimBrand.HasValue && claimBrand.Value != Guid.Empty)
            return claimBrand.Value;

        throw new UnauthorizedAccessException(
            "Brand context required. For platform admins, pass the X-Brand-Id header.");
    }

    private string? Claim(string type) => Principal?.FindFirstValue(type);
    private Guid? ParseGuid(string type) => Guid.TryParse(Claim(type), out var g) ? g : null;
}
