using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.Auth.Commands.PartnerOtpVerify;

/// <summary>
/// Verifies a partner login OTP and mints a partner access token. Reuses the exact OTP crypto +
/// rolling-window lockout as the step-up / customer verify handlers (SEC1/SEC2), scoped to
/// purpose=partner_login. On success it resolves the partner user (and its org) by phone, enforces
/// that both are active, then issues a <c>token_use=partner</c> JWT carrying partner_id + partner_role
/// (no brand_id, no permissions, no refresh token — RaaS MVP).
///
/// This handler runs on the CORE host (the token issuer). The partner_users/partners lookups are
/// read-only; because this path is unauthenticated (no partner context yet), the host allow-lists it
/// for an RLS bypass exactly like the other pre-auth scope-resolving auth paths.
/// </summary>
public sealed class PartnerOtpVerifyHandler : ICommandHandler<PartnerOtpVerifyCommand, PartnerTokenResponse>
{
    private const string ActiveStatus = "active";

    private readonly ICoreDbContext   _db;
    private readonly IJwtTokenService _jwt;
    private readonly JwtSettings      _jwtSettings;
    private readonly OtpSettings      _otpSettings;
    private readonly IHostEnvironment _env;

    public PartnerOtpVerifyHandler(
        ICoreDbContext db,
        IJwtTokenService jwt,
        IOptions<JwtSettings> jwtOptions,
        IOptions<OtpSettings> otpOptions,
        IHostEnvironment env)
    {
        _db          = db;
        _jwt         = jwt;
        _jwtSettings = jwtOptions.Value;
        _otpSettings = otpOptions.Value;
        _env         = env;
    }

    public async Task<PartnerTokenResponse> HandleAsync(PartnerOtpVerifyCommand cmd, CancellationToken ct)
    {
        var phone = cmd.Phone;

        // SEC1: rolling-window lockout BEFORE loading the row (no existence oracle).
        var lockoutWindowCutoff = DateTimeOffset.UtcNow.AddMinutes(-_otpSettings.LockoutWindowMinutes);
        var windowAttempts = await _db.OtpCodes
            .Where(o => o.Identifier     == phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.PartnerLogin
                     && o.CreatedAt      > lockoutWindowCutoff)
            .Select(o => o.Attempts)
            .ToListAsync(ct);

        if (OtpSecurityHelper.ExceedsLockoutThreshold(
                OtpSecurityHelper.SumWindowAttempts(windowAttempts), _otpSettings.LockoutThreshold))
        {
            throw new BusinessRuleException(
                $"Too many attempts. Try again in {_otpSettings.LockoutDurationMinutes} minutes.");
        }

        var otpCode = await _db.OtpCodes
            .Where(o => o.Identifier     == phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.PartnerLogin
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otpCode is null)
            throw new UnauthorizedAccessException("OTP not found or expired.");
        if (otpCode.Attempts >= otpCode.MaxAttempts)
            throw new UnauthorizedAccessException("Maximum OTP attempts exceeded.");

        // SEC2: salted HMAC verify (test master code accepted only outside production).
        var hmacKey = OtpSecurityHelper.ResolveHmacKey(_otpSettings, _env.IsDevelopment());
        var isValid = OtpSecurityHelper.IsTestCodeAccepted(_otpSettings.TestCode, _env.IsProduction(), cmd.Code.Trim())
                   || OtpSecurityHelper.VerifyCode(hmacKey, otpCode.CodeSalt, otpCode.CodeHash, cmd.Code.Trim());

        if (!isValid)
        {
            otpCode.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        otpCode.VerifiedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // OTP proven — resolve the partner user (+ its org) now. /otp/send only issues codes for
        // existing partner users, so a missing row here means a concurrent removal → treat as denied.
        var partnerUser = await _db.PartnerUsers
            .Include(pu => pu.Partner)
            .FirstOrDefaultAsync(pu => pu.PhoneE164 == phone, ct)
            ?? throw new ForbiddenException("No active partner account for this phone.");

        // Block a suspended/invited user or a suspended/terminated org (see the partner_users /
        // partners status CHECK constraints).
        if (!string.Equals(partnerUser.Status, ActiveStatus, StringComparison.Ordinal)
            || !string.Equals(partnerUser.Partner.Status, ActiveStatus, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Partner account is not active.");
        }

        var accessToken = _jwt.CreatePartnerAccessToken(new PartnerTokenClaims(
            PartnerUserId: partnerUser.Id,
            PartnerId:     partnerUser.PartnerId,
            PartnerRole:   partnerUser.PartnerRole,
            Phone:         phone));

        return new PartnerTokenResponse(
            AccessToken:      accessToken,
            ExpiresInSeconds: _jwtSettings.AccessMinutes * 60);
    }
}
