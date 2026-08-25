using System.Net;
using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Dtos;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth;
using Microsoft.EntityFrameworkCore;
using CustomerEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer;
using RefreshTokenEntity = laundryghar.SharedDataModel.Entities.IdentityAccess.RefreshToken;

namespace core.Application.Identity.Auth.Common;

/// <summary>
/// Mints a customer session: access JWT + root refresh token + login_history row.
///
/// Extracted because three flows now end the same way — OTP verify, Google sign-in and PIN
/// unlock. Keeping one implementation means a change to token shape, refresh-family handling
/// or audit logging cannot silently apply to only some of the ways a customer signs in.
/// </summary>
public static class CustomerSessionIssuer
{
    /// <param name="authMethod">One of the <see cref="laundryghar.SharedDataModel.Enums.AuthMethod"/> constants.</param>
    /// <param name="identifier">What the customer authenticated with, for the audit trail.</param>
    /// <param name="isNewCustomer">Whether the calling flow just created this customer row.</param>
    public static async Task<CustomerTokenResponse> IssueAsync(
        ICoreDbContext db,
        IJwtTokenService jwt,
        IRefreshTokenRepository refreshTokens,
        JwtSettings jwtSettings,
        CustomerEntity customer,
        string authMethod,
        string identifier,
        string? ipAddressRaw,
        string? userAgent,
        bool isNewCustomer,
        CancellationToken ct)
    {
        var ipAddress = string.IsNullOrEmpty(ipAddressRaw) ? null
            : IPAddress.TryParse(ipAddressRaw, out var ip) ? ip : null;

        var accessToken = jwt.CreateCustomerAccessToken(new CustomerTokenClaims(
            CustomerId: customer.Id,
            BrandId:    customer.BrandId,
            Phone:      customer.PhoneE164));

        var rawRefresh = jwt.GenerateRefreshTokenRaw();
        var tokenHash  = jwt.HashRefreshToken(rawRefresh);
        var rtId       = Guid.NewGuid();

        var refreshToken = new RefreshTokenEntity
        {
            Id         = rtId,
            CustomerId = customer.Id,
            TokenHash  = tokenHash,
            FamilyId   = rtId,           // root: family_id = own id
            IpAddress  = ipAddress,
            UserAgent  = userAgent,
            IssuedAt   = DateTimeOffset.UtcNow,
            ExpiresAt  = DateTimeOffset.UtcNow.AddDays(jwtSettings.RefreshDays),
            CreatedAt  = DateTimeOffset.UtcNow
        };

        db.LoginHistories.Add(new LoginHistory
        {
            Id         = Guid.NewGuid(),
            CustomerId = customer.Id,
            Identifier = identifier,
            AuthMethod = authMethod,
            Success    = true,
            IpAddress  = ipAddress,
            UserAgent  = userAgent,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt  = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);

        // Root refresh-token insert uses raw parameterized SQL (self-referential family_id FK).
        await refreshTokens.InsertRootAsync(refreshToken, ct);

        return new CustomerTokenResponse(
            AccessToken:      accessToken,
            RefreshToken:     rawRefresh,
            ExpiresInSeconds: jwtSettings.AccessMinutes * 60,
            IsNewCustomer:    isNewCustomer,
            NeedsPhone:       string.IsNullOrEmpty(customer.PhoneE164),
            HasPin:           !string.IsNullOrEmpty(customer.PinHash),
            ProfileComplete:  CustomerProfileCompletion.IsComplete(customer));
    }

    /// <summary>
    /// Generates a brand-unique customer_code. Shared by every flow that can create a
    /// customer, so the alphabet (no ambiguous 0/O/1/I) stays consistent.
    /// </summary>
    public static async Task<string> GenerateUniqueCodeAsync(
        ICoreDbContext db, Guid brandId, CancellationToken ct)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var code = new string(Enumerable.Range(0, 10)
                .Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)])
                .ToArray());

            var exists = await db.Customers
                .AnyAsync(c => c.BrandId == brandId && c.CustomerCode == code, ct);

            if (!exists) return code;
        }
        return Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }
}
