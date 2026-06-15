using System.Net;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.Auth.Commands.CustomerOtpSend;

/// <summary>
/// Sends a 6-digit OTP to a customer phone number under a specific brand.
/// The brand is resolved inside the handler (header → body brandCode → config default).
///
/// Security properties enforced:
///   H1  — Cooldown and invalidation queries are brand-scoped (phone + brand_id).
///   L2  — Phone number is masked in logs outside Development.
///   SEC1 — Rolling-window lockout: ≥ LockoutThreshold failed verifies for the phone
///           (brand-scoped) within LockoutWindowMinutes → block send for LockoutDurationMinutes.
///           Resend-cycling does not bypass this because we sum Attempts on ALL rows in the window.
///   SEC2 — HMAC-SHA256 with per-row random salt.
/// </summary>
public sealed class CustomerOtpSendHandler : ICommandHandler<CustomerOtpSendCommand, OtpSentResponse>
{
    private const int CodeLength = 6;

    private readonly ICoreDbContext   _db;
    private readonly IOtpSender       _sender;
    private readonly OtpSettings      _settings;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration   _config;
    private readonly ILogger<CustomerOtpSendHandler> _logger;

    public CustomerOtpSendHandler(
        ICoreDbContext db,
        IOtpSender sender,
        IOptions<OtpSettings> otpOptions,
        IHostEnvironment env,
        IConfiguration config,
        ILogger<CustomerOtpSendHandler> logger)
    {
        _db       = db;
        _sender   = sender;
        _settings = otpOptions.Value;
        _env      = env;
        _config   = config;
        _logger   = logger;
    }

    public async Task<OtpSentResponse> HandleAsync(CustomerOtpSendCommand cmd, CancellationToken ct)
    {
        // Brand resolution (header → body brandCode → config default → "LG-MAIN").
        var brandId = await CustomerBrandResolver.ResolveAsync(
            _db, _config, cmd.RawHeaderBrandId, cmd.BodyBrandCode, ct);

        // SEC1: Rolling-window lockout — brand-scoped.
        // Sum Attempts on ALL rows (verified, expired, active) for this (phone, brand)
        // within the window so that resend-cycling cannot reset the counter.
        var lockoutWindowCutoff = DateTimeOffset.UtcNow.AddMinutes(-_settings.LockoutWindowMinutes);
        var windowAttempts = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.Login
                     && o.ReferenceId    == brandId      // H1: brand-scoped
                     && o.ReferenceType  == "brand"
                     && o.CreatedAt      > lockoutWindowCutoff)
            .Select(o => o.Attempts)
            .ToListAsync(ct);

        var totalAttempts = OtpSecurityHelper.SumWindowAttempts(windowAttempts);
        if (OtpSecurityHelper.ExceedsLockoutThreshold(totalAttempts, _settings.LockoutThreshold))
        {
            throw new BusinessRuleException(
                $"Too many attempts. Try again in {_settings.LockoutDurationMinutes} minutes.");
        }

        // H1: Both the cooldown check AND the invalidation query are scoped to
        // (phone, brand) — a send for Brand B must not affect Brand A's pending OTP
        // for the same phone, and the resend cooldown is per-(phone, brand) pair.
        var cooldownCutoff = DateTimeOffset.UtcNow.AddSeconds(-_settings.ResendCooldownSeconds);
        var recent = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.Login
                     && o.ReferenceId    == brandId      // H1: brand-scoped
                     && o.ReferenceType  == "brand"      // H1: brand-scoped
                     && o.VerifiedAt     == null
                     && o.CreatedAt      > cooldownCutoff)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (recent is not null)
        {
            var wait = (int)Math.Ceiling(
                (recent.CreatedAt.AddSeconds(_settings.ResendCooldownSeconds) - DateTimeOffset.UtcNow)
                .TotalSeconds);
            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["phone"] = [$"Please wait {wait} seconds before requesting a new OTP."]
                });
        }

        // H1: Expire only pending OTPs for this (phone, brand) — not all brands
        var existing = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.Login
                     && o.ReferenceId    == brandId      // H1: brand-scoped
                     && o.ReferenceType  == "brand"      // H1: brand-scoped
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        foreach (var old in existing)
            old.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Optionally associate with existing customer
        var customerId = await _db.Customers
            .Where(c => c.BrandId == brandId && c.PhoneE164 == cmd.Phone)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        // SEC2: Generate OTP with HMAC-SHA256 + per-row random salt
        var plainCode = GenerateNumericCode(CodeLength);
        var salt      = OtpSecurityHelper.GenerateSalt();
        var hmacKey   = OtpSecurityHelper.ResolveHmacKey(_settings, _env.IsDevelopment());
        var codeHash  = OtpSecurityHelper.ComputeHmac(hmacKey, salt, plainCode);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.TtlMinutes);

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        // ReferenceId stores the brand context so verify can resolve the correct brand.
        _db.OtpCodes.Add(new OtpCode
        {
            Id             = Guid.NewGuid(),
            Purpose        = OtpPurpose.Login,
            Identifier     = cmd.Phone,
            IdentifierType = "phone",
            CodeHash       = codeHash,
            CodeSalt       = salt,
            CustomerId     = customerId,
            ReferenceId    = brandId,
            ReferenceType  = "brand",
            Attempts       = 0,
            MaxAttempts    = (short)_settings.MaxAttempts,
            ExpiresAt      = expiresAt,
            IpAddress      = ipAddress,
            UserAgent      = cmd.UserAgent,
            CreatedAt      = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        // L2: mask PII outside Development — full phone visible in dev logs only
        var logPhone = _env.IsDevelopment() ? cmd.Phone : MaskPhone(cmd.Phone);
        _logger.LogDebug(
            "[CUSTOMER-OTP] Phone={Phone} Brand={BrandId} ExpiresAt={ExpiresAt}",
            logPhone, brandId, expiresAt);

        await _sender.SendAsync(cmd.Phone, "phone", plainCode, OtpPurpose.Login, ct, brandId: brandId);

        return new OtpSentResponse("OTP sent successfully.", expiresAt);
    }

    private static string GenerateNumericCode(int length)
    {
        var max    = (int)Math.Pow(10, length);
        var random = System.Security.Cryptography.RandomNumberGenerator.GetInt32(max);
        return random.ToString().PadLeft(length, '0');
    }

    /// <summary>
    /// L2: Masks all but the country code prefix and last 4 digits.
    /// E.g. +919876543210 → +91XXXXXX3210
    /// Falls back to a fully masked string for non-E.164 values.
    /// </summary>
    internal static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone)) return "****";

        if (phone.Length > 6 && phone.StartsWith('+'))
        {
            int ccEnd = 1;
            while (ccEnd < phone.Length && ccEnd <= 3 && char.IsDigit(phone[ccEnd]))
                ccEnd++;

            var prefix  = phone[..ccEnd];
            var last4   = phone.Length >= 4 ? phone[^4..] : phone;
            var midLen  = phone.Length - ccEnd - last4.Length;
            var masked  = midLen > 0 ? new string('X', midLen) : string.Empty;
            return $"{prefix}{masked}{last4}";
        }

        return phone.Length <= 4 ? "****" : $"****{phone[^4..]}";
    }
}
