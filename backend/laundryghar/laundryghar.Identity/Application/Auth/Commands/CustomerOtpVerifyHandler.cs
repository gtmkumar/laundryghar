using System.Security.Cryptography;
using laundryghar.Identity.Application.Auth.Dtos;
using laundryghar.Identity.Infrastructure.Auth;
using laundryghar.Identity.Infrastructure.Services;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Application.Auth.Commands;

/// <summary>
/// Verifies a customer OTP. On success:
/// - Find-or-create the customer row (signup-on-first-login).
/// - L1: new customers default all marketing opt-ins to false (DPDP affirmative consent).
/// - Issues a customer JWT (token_use=customer) + rotating refresh token.
/// - L2: phone masked in logs outside Development.
/// - Writes login_history with customer_id.
/// </summary>
public sealed class CustomerOtpVerifyHandler : IRequestHandler<CustomerOtpVerifyCommand, CustomerTokenResponse>
{
    private readonly LaundryGharDbContext _db;
    private readonly IJwtTokenService     _jwt;
    private readonly JwtSettings          _jwtSettings;
    private readonly IHostEnvironment     _env;
    private readonly ILogger<CustomerOtpVerifyHandler> _logger;

    public CustomerOtpVerifyHandler(
        LaundryGharDbContext db,
        IJwtTokenService jwt,
        IOptions<JwtSettings> jwtOptions,
        IHostEnvironment env,
        ILogger<CustomerOtpVerifyHandler> logger)
    {
        _db          = db;
        _jwt         = jwt;
        _jwtSettings = jwtOptions.Value;
        _env         = env;
        _logger      = logger;
    }

    public async Task<CustomerTokenResponse> Handle(CustomerOtpVerifyCommand cmd, CancellationToken ct)
    {
        var codeHash = HashOtp(cmd.Code.Trim());

        // Find the most recent valid OTP for this phone scoped to this brand
        var otpCode = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.Login
                     && o.ReferenceId    == cmd.ResolvedBrandId
                     && o.ReferenceType  == "brand"
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

        otpCode.VerifiedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // ── Find or create customer (signup-on-first-login) ───────────────────
        var customer = await _db.Customers
            .Where(c => c.BrandId == cmd.ResolvedBrandId && c.PhoneE164 == cmd.Phone)
            .FirstOrDefaultAsync(ct);

        bool isNew = customer is null;
        if (isNew)
        {
            var customerCode = await GenerateUniqueCodeAsync(cmd.ResolvedBrandId, ct);
            customer = new laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer
            {
                Id               = Guid.NewGuid(),
                BrandId          = cmd.ResolvedBrandId,
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
                // Affirmative consent for each channel is captured later via
                // the dedicated consent endpoints (dpdp_consents).
                // Transactional communications (OTP, order status) are separately
                // justified and do not depend on these flags.
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
            // L2: mask phone PII outside Development
            var logPhone = _env.IsDevelopment() ? cmd.Phone : CustomerOtpSendHandler.MaskPhone(cmd.Phone);
            _logger.LogInformation(
                "Created new customer {CustomerId} for phone {Phone} brand {BrandId}.",
                customer.Id, logPhone, cmd.ResolvedBrandId);
        }
        else
        {
            // Update last active + verify phone if not yet set
            if (customer!.PhoneVerifiedAt is null)
                customer.PhoneVerifiedAt = DateTimeOffset.UtcNow;
            customer.LastActiveAt = DateTimeOffset.UtcNow;
            customer.UpdatedAt    = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // Update OTP's customer_id now that we have it
        otpCode.CustomerId = customer.Id;
        await _db.SaveChangesAsync(ct);

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        // ── Issue customer JWT ─────────────────────────────────────────────────
        var accessToken = _jwt.CreateCustomerAccessToken(new CustomerTokenClaims(
            CustomerId: customer.Id,
            BrandId:    customer.BrandId,
            Phone:      customer.PhoneE164));

        // ── Refresh token (customer_id path) ──────────────────────────────────
        var rawRefresh = _jwt.GenerateRefreshTokenRaw();
        var tokenHash  = _jwt.HashRefreshToken(rawRefresh);
        var rtId       = Guid.NewGuid();

        var refreshToken = new RefreshToken
        {
            Id         = rtId,
            CustomerId = customer.Id,
            TokenHash  = tokenHash,
            FamilyId   = rtId,   // root: self-referential
            IpAddress  = ipAddress,
            UserAgent  = cmd.UserAgent,
            IssuedAt   = DateTimeOffset.UtcNow,
            ExpiresAt  = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshDays),
            CreatedAt  = DateTimeOffset.UtcNow
        };

        await RefreshTokenRepository.InsertRootAsync(_db, refreshToken, ct);

        // ── Login history ──────────────────────────────────────────────────────
        _db.LoginHistories.Add(new LoginHistory
        {
            Id         = Guid.NewGuid(),
            CustomerId = customer.Id,
            Identifier = cmd.Phone,
            AuthMethod = AuthMethod.Otp,
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
            ExpiresInSeconds: _jwtSettings.AccessMinutes * 60,
            IsNewCustomer:    isNew);
    }

    /// <summary>Generates a unique 10-char alphanumeric customer code for the brand.</summary>
    private async Task<string> GenerateUniqueCodeAsync(Guid brandId, CancellationToken ct)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var code = new string(Enumerable.Range(0, 10)
                .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
                .ToArray());

            var exists = await _db.Customers
                .AnyAsync(c => c.BrandId == brandId && c.CustomerCode == code, ct);

            if (!exists) return code;
        }
        // Fallback: Guid-based code (collision-safe but less pretty)
        return Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }

    private static string HashOtp(string code)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
