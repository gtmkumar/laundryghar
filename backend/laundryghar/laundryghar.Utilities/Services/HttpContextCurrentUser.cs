using System.Security.Claims;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
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

    public IReadOnlyCollection<ScopeNode> ScopeNodes
    {
        get
        {
            var raw = Claim("scope_nodes");
            if (string.IsNullOrEmpty(raw)) return Array.Empty<ScopeNode>();
            return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Select(ScopeNode.TryParse)
                      .Where(n => n.HasValue)
                      .Select(n => n!.Value)
                      .ToArray();
        }
    }

    public bool IsWithinScope(Guid? brandId = null, Guid? franchiseId = null, Guid? storeId = null, Guid? warehouseId = null)
    {
        // Platform operators are unbounded (they already bypass RLS + permission checks).
        if (IsPlatformAdmin) return true;

        // Backward-compat / rollout safety: a token with NO scope_nodes claim at all is either
        // pre-dating this feature or belongs to a principal with no memberships (which then holds
        // no permissions and can't reach a guarded handler anyway). Since the JWT is signed the
        // claim cannot be stripped, so an absent claim reliably means "not enforceable" → allow.
        // A present claim is enforced normally: deny unless one of its nodes matches the target.
        if (Claim("scope_nodes") is null) return true;

        foreach (var node in ScopeNodes)
        {
            switch (node.ScopeType)
            {
                case SharedDataModel.Enums.ScopeType.Platform:
                    return true; // platform membership is an ancestor of every node
                case SharedDataModel.Enums.ScopeType.Brand when Matches(node.ScopeId, brandId):
                case SharedDataModel.Enums.ScopeType.Franchise when Matches(node.ScopeId, franchiseId):
                case SharedDataModel.Enums.ScopeType.Store when Matches(node.ScopeId, storeId):
                case SharedDataModel.Enums.ScopeType.Warehouse when Matches(node.ScopeId, warehouseId):
                    return true;
            }
        }
        return false;

        static bool Matches(Guid? nodeId, Guid? targetId)
            => nodeId is { } n && targetId is { } t && n == t;
    }

    public Guid? TryGetBrandId()
    {
        if (_accessor.HttpContext?.Items.TryGetValue("brand_id_override", out var overrideVal) == true
            && overrideVal is Guid overrideGuid && overrideGuid != Guid.Empty)
            return overrideGuid;

        return BrandId is { } b && b != Guid.Empty ? b : null;
    }

    public Guid RequireBrandId()
        => TryGetBrandId()
           ?? throw new UnauthorizedAccessException(
               "Brand context required. For platform admins, pass the X-Brand-Id header.");

    private string? Claim(string type) => Principal?.FindFirstValue(type);
    private Guid? ParseGuid(string type) => Guid.TryParse(Claim(type), out var g) ? g : null;
}
