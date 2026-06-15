using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using core.Application.Common;
using core.Application.Common.Interfaces;
using laundryghar.Utilities.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace core.Infrastructure.Auth;

/// <summary>
/// HS256-backed JWT service for the dev phase.
/// Swap SigningCredentials to RS256 (X509Certificate2) for production.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings     _settings;
    private readonly IJwtKeyProvider _keys;

    public JwtTokenService(IOptions<JwtSettings> options, IJwtKeyProvider keys)
    {
        _settings = options.Value;
        _keys     = keys;
    }

    public string CreateAccessToken(TokenClaims claims)
    {
        // RS256 — signed with the RSA private key; the kid travels in the JWT header.
        var creds = new SigningCredentials(_keys.SigningKey, SecurityAlgorithms.RsaSha256);

        var claimsList = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, claims.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // Pinned contract: token_use=user distinguishes system tokens from customer tokens.
            // The Catalog service (and all downstream services) rely on this claim.
            new("token_use", TokenClaims.TokenUseValue),
            new("user_type", claims.UserType),
        };

        if (!string.IsNullOrEmpty(claims.Email))
            claimsList.Add(new Claim(JwtRegisteredClaimNames.Email, claims.Email));
        if (!string.IsNullOrEmpty(claims.Phone))
            claimsList.Add(new Claim("phone", claims.Phone));
        if (claims.ScopeType is not null)
            claimsList.Add(new Claim("scope_type", claims.ScopeType));
        if (claims.ScopeId.HasValue)
            claimsList.Add(new Claim("scope_id", claims.ScopeId.Value.ToString()));
        if (claims.BrandId.HasValue)
            claimsList.Add(new Claim("brand_id", claims.BrandId.Value.ToString()));
        if (claims.FranchiseId.HasValue)
            claimsList.Add(new Claim("franchise_id", claims.FranchiseId.Value.ToString()));
        if (claims.StoreId.HasValue)
            claimsList.Add(new Claim("store_id", claims.StoreId.Value.ToString()));
        if (!string.IsNullOrEmpty(claims.Permissions))
            claimsList.Add(new Claim("permissions", claims.Permissions));

        return WriteToken(claimsList, creds);
    }

    public string CreateCustomerAccessToken(CustomerTokenClaims claims)
    {
        var creds = new SigningCredentials(_keys.SigningKey, SecurityAlgorithms.RsaSha256);

        // Pinned contract (Catalog service depends on exact claim names):
        //   sub=customer_id, token_use=customer, brand_id, phone
        var claimsList = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, claims.CustomerId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("token_use", CustomerTokenClaims.TokenUseValue),
            new("brand_id",  claims.BrandId.ToString()),
            new("phone",     claims.Phone),
        };

        return WriteToken(claimsList, creds);
    }

    /// <inheritdoc/>
    public string CreateOAuthCustomerAccessToken(CustomerTokenClaims claims, string scope)
    {
        var creds = new SigningCredentials(_keys.SigningKey, SecurityAlgorithms.RsaSha256);

        // OAuth 2.1 / MCP path — token_use=customer_mcp distinguishes this from the regular
        // customer token so the MCP CustomerOnly policy can enforce scope independently.
        // The Catalog/Orders services reject this token (they only accept token_use=customer),
        // which is intentional: OAuth tokens must not grant access to non-MCP endpoints.
        var claimsList = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, claims.CustomerId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("token_use", CustomerTokenClaims.OAuthTokenUseValue),
            new("brand_id",  claims.BrandId.ToString()),
            new("phone",     claims.Phone),
            new("scope",     scope),
        };

        return WriteToken(claimsList, creds);
    }

    private string WriteToken(IEnumerable<Claim> claims, SigningCredentials creds)
    {
        var token = new JwtSecurityToken(
            issuer:             _settings.Issuer,
            audience:           _settings.Audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddMinutes(_settings.AccessMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshTokenRaw()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    public string HashRefreshToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
