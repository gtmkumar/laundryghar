namespace laundryghar.Catalog.Infrastructure.Auth;

/// <summary>
/// Claims for a system user (staff/admin/rider) JWT.
/// token_use is always "user" — used to reject customer tokens on admin endpoints.
/// Pinned contract: must match laundryghar.Identity TokenClaims exactly.
/// </summary>
public sealed record TokenClaims(
    Guid UserId,
    string UserType,
    string? Email,
    string? Phone,
    string? ScopeType,
    Guid? ScopeId,
    Guid? BrandId,
    Guid? FranchiseId,
    Guid? StoreId,
    string Permissions
)
{
    /// <summary>Fixed token_use value for system users. Pinned for cross-service contract.</summary>
    public const string TokenUseValue = "user";
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
    /// <summary>Fixed token_use value for customers. Pinned for cross-service contract.</summary>
    public const string TokenUseValue = "customer";
}
