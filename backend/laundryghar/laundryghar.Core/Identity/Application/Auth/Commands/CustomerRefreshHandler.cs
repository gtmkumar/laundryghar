using laundryghar.Identity.Application.Auth.Dtos;
using laundryghar.Identity.Infrastructure.Auth;
using MediatR;
using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Application.Auth.Commands;

/// <summary>Rotates a customer refresh token with the same reuse-detection + family-revocation logic as the system refresh handler.</summary>
public sealed class CustomerRefreshHandler : IRequestHandler<CustomerRefreshCommand, CustomerTokenResponse>
{
    private readonly LaundryGharDbContext _db;
    private readonly IJwtTokenService     _jwt;
    private readonly JwtSettings          _jwtSettings;

    public CustomerRefreshHandler(
        LaundryGharDbContext db,
        IJwtTokenService jwt,
        IOptions<JwtSettings> jwtOptions)
    {
        _db          = db;
        _jwt         = jwt;
        _jwtSettings = jwtOptions.Value;
    }

    public async Task<CustomerTokenResponse> Handle(CustomerRefreshCommand cmd, CancellationToken ct)
    {
        var tokenHash = _jwt.HashRefreshToken(cmd.RawRefreshToken);

        var existing = await _db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
            throw new UnauthorizedAccessException("Invalid refresh token.");

        // Must be a customer token (customer_id set, user_id null)
        if (!existing.CustomerId.HasValue || existing.UserId.HasValue)
            throw new UnauthorizedAccessException("This token is not a customer token.");

        if (existing.RevokedAt.HasValue)
        {
            await RevokeFamilyAsync(existing.FamilyId, "reuse_detected", ct);
            throw new UnauthorizedAccessException("Refresh token reuse detected. Please log in again.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Refresh token expired.");

        var customer = await _db.Customers.FindAsync([existing.CustomerId.Value], ct);
        if (customer is null || customer.Status != "active")
            throw new UnauthorizedAccessException("Customer account is not active.");

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        existing.RevokedAt     = DateTimeOffset.UtcNow;
        existing.RevokedReason = "rotated";

        var accessToken  = _jwt.CreateCustomerAccessToken(new CustomerTokenClaims(
            CustomerId: customer.Id,
            BrandId:    customer.BrandId,
            Phone:      customer.PhoneE164));

        var rawRefresh   = _jwt.GenerateRefreshTokenRaw();
        var newTokenHash = _jwt.HashRefreshToken(rawRefresh);

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id            = Guid.NewGuid(),
            CustomerId    = customer.Id,
            TokenHash     = newTokenHash,
            FamilyId      = existing.FamilyId,
            ParentTokenId = existing.Id,
            IpAddress     = ipAddress,
            UserAgent     = cmd.UserAgent,
            IssuedAt      = DateTimeOffset.UtcNow,
            ExpiresAt     = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshDays),
            CreatedAt     = DateTimeOffset.UtcNow
        });

        _db.LoginHistories.Add(new LoginHistory
        {
            Id         = Guid.NewGuid(),
            CustomerId = customer.Id,
            Identifier = customer.PhoneE164,
            AuthMethod = AuthMethod.Refresh,
            Success    = true,
            IpAddress  = ipAddress,
            UserAgent  = cmd.UserAgent,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt  = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        return new CustomerTokenResponse(
            AccessToken:      accessToken,
            RefreshToken:     rawRefresh,
            ExpiresInSeconds: _jwtSettings.AccessMinutes * 60);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        tokens.ForEach(t => { t.RevokedAt = DateTimeOffset.UtcNow; t.RevokedReason = reason; });
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Revokes all tokens in a customer refresh token family.</summary>
public sealed class CustomerLogoutHandler : IRequestHandler<CustomerLogoutCommand, Unit>
{
    private readonly LaundryGharDbContext _db;
    private readonly IJwtTokenService     _jwt;

    public CustomerLogoutHandler(LaundryGharDbContext db, IJwtTokenService jwt)
    {
        _db  = db;
        _jwt = jwt;
    }

    public async Task<Unit> Handle(CustomerLogoutCommand cmd, CancellationToken ct)
    {
        var tokenHash = _jwt.HashRefreshToken(cmd.RawRefreshToken);
        var token     = await _db.RefreshTokens.Where(t => t.TokenHash == tokenHash).FirstOrDefaultAsync(ct);
        if (token is null) return Unit.Value;

        var family = await _db.RefreshTokens
            .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
            .ToListAsync(ct);

        family.ForEach(t => { t.RevokedAt = DateTimeOffset.UtcNow; t.RevokedReason = "logout"; });
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
