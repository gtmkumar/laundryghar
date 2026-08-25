using System.Net;
using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Commands.CustomerOtpSend;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Auth;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CustomerEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer;
using RefreshTokenEntity = laundryghar.SharedDataModel.Entities.IdentityAccess.RefreshToken;

namespace core.Application.Identity.Auth.Commands.CustomerOtpVerify;

/// <summary>
/// Verifies a customer OTP. On success:
/// - Find-or-create the customer row (signup-on-first-login).
/// - L1: new customers default all marketing opt-ins to false (DPDP affirmative consent).
/// - Issues a customer JWT (token_use=customer) + rotating refresh token.
/// - L2: phone masked in logs outside Development.
/// - Writes login_history with customer_id.
///
/// Security properties enforced:
///   SEC1 — Rolling-window lockout check before loading the OTP row (no existence oracle).
///   SEC2 — HMAC-SHA256 verify with per-row salt; falls back to legacy SHA-256 for
///           pre-migration rows (code_salt IS NULL).
/// </summary>
public sealed class CustomerOtpVerifyHandler : ICommandHandler<CustomerOtpVerifyCommand, CustomerTokenResponse>
{
    private readonly ICoreDbContext          _db;
    private readonly IJwtTokenService        _jwt;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly JwtSettings             _jwtSettings;
    private readonly OtpSettings             _otpSettings;
    private readonly IHostEnvironment        _env;
    private readonly IConfiguration          _config;
    private readonly ILogger<CustomerOtpVerifyHandler> _logger;

    public CustomerOtpVerifyHandler(
        ICoreDbContext db,
        IJwtTokenService jwt,
        IRefreshTokenRepository refreshTokens,
        IOptions<JwtSettings> jwtOptions,
        IOptions<OtpSettings> otpOptions,
        IHostEnvironment env,
        IConfiguration config,
        ILogger<CustomerOtpVerifyHandler> logger)
    {
        _db            = db;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _jwtSettings   = jwtOptions.Value;
        _otpSettings   = otpOptions.Value;
        _env           = env;
        _config        = config;
        _logger        = logger;
    }

    public async Task<CustomerTokenResponse> HandleAsync(CustomerOtpVerifyCommand cmd, CancellationToken ct)
    {
        // Brand resolution (header → body brandCode → config default → "LG-MAIN").
        var brandId = await CustomerBrandResolver.ResolveAsync(
            _db, _config, cmd.RawHeaderBrandId, cmd.BodyBrandCode, ct);

        // SEC1: Rolling-window lockout — brand-scoped, checked BEFORE loading the OTP row.
        var lockoutWindowCutoff = DateTimeOffset.UtcNow.AddMinutes(-_otpSettings.LockoutWindowMinutes);
        var windowAttempts = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.Login
                     && o.ReferenceId    == brandId
                     && o.ReferenceType  == "brand"
                     && o.CreatedAt      > lockoutWindowCutoff)
            .Select(o => o.Attempts)
            .ToListAsync(ct);

        var totalAttempts = OtpSecurityHelper.SumWindowAttempts(windowAttempts);
        if (OtpSecurityHelper.ExceedsLockoutThreshold(totalAttempts, _otpSettings.LockoutThreshold))
        {
            throw new BusinessRuleException(
                $"Too many attempts. Try again in {_otpSettings.LockoutDurationMinutes} minutes.");
        }

        // Find the most recent valid OTP for this phone scoped to this brand
        var otpCode = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.Login
                     && o.ReferenceId    == brandId
                     && o.ReferenceType  == "brand"
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otpCode is null)
            throw new UnauthorizedAccessException("OTP not found or expired.");

        if (otpCode.Attempts >= otpCode.MaxAttempts)
            throw new UnauthorizedAccessException("Maximum OTP attempts exceeded.");

        // SEC2: Verify using salted HMAC, falling back to legacy SHA-256 for pre-migration rows.
        // Non-production additionally accepts Otp:CustomerTestCode (the 4-digit customer master
        // code, e.g. "1234"). Otp:TestCode is deliberately NOT accepted here — it is the 6-digit
        // staff/rider code and would not match this flow's length anyway.
        var hmacKey = OtpSecurityHelper.ResolveHmacKey(_otpSettings, _env.IsDevelopment());
        var isValid = OtpSecurityHelper.IsTestCodeAccepted(_otpSettings.CustomerTestCode, _env.IsProduction(), cmd.Code.Trim())
                   || OtpSecurityHelper.VerifyCode(
                          hmacKey,
                          otpCode.CodeSalt,
                          otpCode.CodeHash,
                          cmd.Code.Trim());

        if (!isValid)
        {
            otpCode.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        otpCode.VerifiedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // ── Find or create customer (signup-on-first-login) ───────────────────
        var customer = await _db.Customers
            .Where(c => c.BrandId == brandId && c.PhoneE164 == cmd.Phone)
            .FirstOrDefaultAsync(ct);

        bool isNew = customer is null;
        if (isNew)
        {
            var customerCode = await CustomerSessionIssuer.GenerateUniqueCodeAsync(_db, brandId, ct);
            customer = new CustomerEntity
            {
                Id               = Guid.NewGuid(),
                BrandId          = brandId,
                CustomerCode     = customerCode,
                PhoneE164        = cmd.Phone,
                PhoneVerifiedAt  = DateTimeOffset.UtcNow,
                Locale           = "en-IN",
                Timezone         = "Asia/Kolkata",
                Status           = "active",
                Metadata         = "{}",
                Tags             = [],
                LifetimeOrders   = 0,
                LifetimeSpend    = 0,
                LoyaltyPointsBalance = 0,
                WalletBalance    = 0,
                // L1 (DPDP Act 2023): all marketing-class opt-ins default to false.
                MarketingOptIn   = false,
                SmsOptIn         = false,
                WhatsappOptIn    = false,
                EmailOptIn       = false,
                PushOptIn        = false,
                CreatedAt        = DateTimeOffset.UtcNow,
                UpdatedAt        = DateTimeOffset.UtcNow,
                Version          = 1
            };
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync(ct);
            var logPhone = _env.IsDevelopment() ? cmd.Phone : CustomerOtpSendHandler.MaskPhone(cmd.Phone);
            _logger.LogInformation(
                "Created new customer {CustomerId} for phone {Phone} brand {BrandId}.",
                customer.Id, logPhone, brandId);
        }
        else
        {
            if (customer!.PhoneVerifiedAt is null)
                customer.PhoneVerifiedAt = DateTimeOffset.UtcNow;
            customer.LastActiveAt = DateTimeOffset.UtcNow;
            customer.UpdatedAt    = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // Update OTP's customer_id now that we have it
        otpCode.CustomerId = customer.Id;
        await _db.SaveChangesAsync(ct);

        // ── Issue the session (JWT + refresh + login_history) ─────────────────
        return await CustomerSessionIssuer.IssueAsync(
            _db, _jwt, _refreshTokens, _jwtSettings,
            customer,
            authMethod:    AuthMethod.Otp,
            identifier:    cmd.Phone,
            ipAddressRaw:  cmd.IpAddress,
            userAgent:     cmd.UserAgent,
            isNewCustomer: isNew,
            ct:            ct);
    }
}
