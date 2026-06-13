namespace laundryghar.Engagement.Infrastructure.Auth;

/// <summary>
/// Claims for a system user (staff/admin/rider) JWT.
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
    public const string TokenUseValue = "user";
}

/// <summary>Claims for a customer JWT. token_use=customer.</summary>
public sealed record CustomerTokenClaims(
    Guid CustomerId,
    Guid BrandId,
    string Phone
)
{
    public const string TokenUseValue = "customer";
}
