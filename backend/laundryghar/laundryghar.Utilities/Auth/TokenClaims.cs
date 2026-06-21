namespace laundryghar.Utilities.Auth;

/// <summary>
/// Claims for a system user (staff/admin/rider) JWT.
/// token_use is always "user" — used to reject customer tokens on admin endpoints.
/// </summary>
public sealed record TokenClaims(
    Guid UserId,
    string UserType,
    string? Email,
    string? Phone,
    // Active scope (from X-Scope header or primary membership)
    string? ScopeType,
    Guid? ScopeId,
    Guid? BrandId,
    Guid? FranchiseId,
    Guid? StoreId,
    // Space-separated permission codes
    string Permissions,
    // Snapshot of the user's perm_version at issuance (for live revocation). Default 0.
    int PermVersion = 0
)
{
    /// <summary>Fixed token_use value for system users. Pinned for Catalog service contract.</summary>
    public const string TokenUseValue = "user";

    /// <summary>JWT claim name carrying <see cref="PermVersion"/>.</summary>
    public const string PermVersionClaim = "perm_ver";
}

/// <summary>
/// Claims for a customer JWT.
/// token_use is always "customer" — used to reject system tokens on customer endpoints.
/// Pinned contract: sub=customer_id, token_use=customer, brand_id, phone. No permissions claim.
/// </summary>
public sealed record CustomerTokenClaims(
    Guid CustomerId,
    Guid BrandId,
    string Phone
)
{
    /// <summary>Fixed token_use value for customers. Pinned for Catalog service contract.</summary>
    public const string TokenUseValue = "customer";

    /// <summary>
    /// token_use value for customers authenticated via OAuth 2.1 (MCP path).
    /// These tokens also carry a <c>scope</c> claim (e.g., "mcp:booking") and are
    /// rejected by Catalog/Orders endpoints (which only accept token_use=customer).
    /// </summary>
    public const string OAuthTokenUseValue = "customer_mcp";
}
