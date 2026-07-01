using laundryghar.Utilities.Auth;

namespace core.Application.Common.Interfaces;

/// <summary>Issues and validates JWT access tokens.</summary>
public interface IJwtTokenService
{
    /// <summary>Creates a signed access JWT for the given system user + scope. Emits token_use=user.</summary>
    string CreateAccessToken(TokenClaims claims);

    /// <summary>Creates a signed access JWT for a customer. Emits token_use=customer.</summary>
    string CreateCustomerAccessToken(CustomerTokenClaims claims);

    /// <summary>
    /// Creates a signed access JWT for a customer authenticated via OAuth 2.1 (MCP path).
    /// Emits <c>token_use=customer_mcp</c> and the granted <c>scope</c> claim so that the MCP
    /// resource server can enforce scope independently of regular customer tokens.
    /// </summary>
    string CreateOAuthCustomerAccessToken(CustomerTokenClaims claims, string scope);

    /// <summary>Creates a signed access JWT for a RaaS partner (docs/rbac.md §9). Emits
    /// <c>token_use=partner</c> + <c>partner_id</c> + <c>partner_role</c>; no brand_id, no permissions.</summary>
    string CreatePartnerAccessToken(PartnerTokenClaims claims);

    /// <summary>Creates a raw (pre-hash) refresh token string. Caller is responsible for hashing and persisting.</summary>
    string GenerateRefreshTokenRaw();

    /// <summary>Hashes a raw refresh token string using SHA-256.</summary>
    string HashRefreshToken(string raw);
}
