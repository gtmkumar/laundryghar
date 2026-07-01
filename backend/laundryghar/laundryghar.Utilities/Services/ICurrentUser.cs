using laundryghar.Utilities.Auth;

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

    /// <summary>Every scope-hierarchy node this user holds an active membership at
    /// (from the JWT <c>scope_nodes</c> claim). Empty for tokens issued before this claim
    /// existed or for non-staff principals. See <see cref="IsWithinScope"/>.</summary>
    IReadOnlyCollection<ScopeNode> ScopeNodes { get; }

    /// <summary>§6 ancestor-or-self boundary check for a target resource identified by its
    /// tenant chain. Returns true iff one of the user's membership nodes is at or above the
    /// resource's node — i.e. the user holds platform scope, OR a brand membership matching
    /// <paramref name="brandId"/>, OR a franchise membership matching <paramref name="franchiseId"/>,
    /// OR a store/warehouse membership matching <paramref name="storeId"/>/<paramref name="warehouseId"/>.
    /// Platform admins always pass. Coarse per-brand RLS is a separate, complementary layer;
    /// this is the sub-brand (franchise/store) boundary the RLS layer cannot express.
    /// Call it in a mutating handler AFTER loading the resource, passing the resource's ids.</summary>
    bool IsWithinScope(Guid? brandId = null, Guid? franchiseId = null, Guid? storeId = null, Guid? warehouseId = null);

    /// <summary>Effective brand without throwing: the X-Brand-Id override (HttpContext
    /// item "brand_id_override") if present, else JWT brand_id, else null. Use for read
    /// paths (e.g. navigation/entitlement) that should degrade gracefully when no brand
    /// context is set rather than 401.</summary>
    Guid? TryGetBrandId();

    /// <summary>Effective brand for write operations. Platform admins: X-Brand-Id override
    /// (HttpContext item "brand_id_override") if present, else JWT brand_id.
    /// Throws <see cref="UnauthorizedAccessException"/> if no brand can be resolved.</summary>
    Guid RequireBrandId();
}
