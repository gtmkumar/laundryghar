using System.Security.Cryptography;
using laundryghar.Identity.Application.Auth.Dtos;
using laundryghar.Identity.Application.Common;
using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.Identity.Infrastructure.Services;
using MediatR;
using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Application.Auth.Commands;

/// <summary>
/// Verifies an OTP code. On success marks verified_at, updates user phone/email verification,
/// issues JWT + refresh token.
/// </summary>
public sealed class OtpVerifyHandler : IRequestHandler<OtpVerifyCommand, OtpVerifiedResponse>
{
    private readonly LaundryGharDbContext _db;
    private readonly IJwtTokenService     _jwt;
    private readonly JwtSettings          _jwtSettings;

    public OtpVerifyHandler(
        LaundryGharDbContext db,
        IJwtTokenService jwt,
        IOptions<JwtSettings> jwtOptions)
    {
        _db          = db;
        _jwt         = jwt;
        _jwtSettings = jwtOptions.Value;
    }

    public async Task<OtpVerifiedResponse> Handle(OtpVerifyCommand cmd, CancellationToken ct)
    {
        var codeHash = HashOtp(cmd.Code.Trim());

        var otpCode = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Identifier
                     && o.IdentifierType == cmd.IdentifierType
                     && o.Purpose        == cmd.Purpose
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otpCode is null)
            throw new UnauthorizedAccessException("OTP not found or expired.");

        if (otpCode.Attempts >= otpCode.MaxAttempts)
            throw new UnauthorizedAccessException("Maximum OTP attempts exceeded.");

        if (!string.Equals(otpCode.CodeHash, codeHash, StringComparison.OrdinalIgnoreCase))
        {
            otpCode.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        // Mark verified
        otpCode.VerifiedAt = DateTimeOffset.UtcNow;

        // Update user verification timestamps
        var user = otpCode.UserId.HasValue
            ? await _db.Users.FindAsync([otpCode.UserId.Value], ct)
            : null;

        if (user is not null)
        {
            if (cmd.IdentifierType == "phone")
                user.PhoneVerifiedAt = DateTimeOffset.UtcNow;
            else if (cmd.IdentifierType == "email")
                user.EmailVerifiedAt = DateTimeOffset.UtcNow;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        if (user is null)
            throw new UnauthorizedAccessException("No user associated with this OTP.");

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        // Issue tokens
        var claims       = await ScopeResolver.BuildTokenClaimsAsync(_db, user, ct: ct);
        var accessToken  = _jwt.CreateAccessToken(claims);
        var rawRefresh   = _jwt.GenerateRefreshTokenRaw();
        var tokenHash    = _jwt.HashRefreshToken(rawRefresh);

        var rtId = Guid.NewGuid();
        var rootRefreshToken = new RefreshToken
        {
            Id        = rtId,
            UserId    = user.Id,
            TokenHash = tokenHash,
            FamilyId  = rtId, // root token
            IpAddress = ipAddress,
            UserAgent = cmd.UserAgent,
            IssuedAt  = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshDays),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.LoginHistories.Add(new LoginHistory
        {
            Id         = Guid.NewGuid(),
            UserId     = user.Id,
            Identifier = cmd.Identifier,
            AuthMethod = AuthMethod.Otp,
            Success    = true,
            IpAddress  = ipAddress,
            UserAgent  = cmd.UserAgent,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt  = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await RefreshTokenRepository.InsertRootAsync(_db, rootRefreshToken, ct);

        return new OtpVerifiedResponse(
            AccessToken:      accessToken,
            RefreshToken:     rawRefresh,
            ExpiresInSeconds: _jwtSettings.AccessMinutes * 60);
    }

    private static string HashOtp(string code)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
