using System.Security.Cryptography;
using laundryghar.Identity.Application.Auth.Dtos;
using laundryghar.Identity.Infrastructure.Auth;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Application.Auth.Commands;

/// <summary>
/// Sends a 6-digit OTP to a customer phone number under a specific brand.
/// H1: cooldown and invalidation queries are brand-scoped.
/// L2: phone number is masked in logs outside Development.
/// </summary>
public sealed class CustomerOtpSendHandler : IRequestHandler<CustomerOtpSendCommand, OtpSentResponse>
{
    private const int CodeLength = 6;

    private readonly LaundryGharDbContext _db;
    private readonly IOtpSender           _sender;
    private readonly OtpSettings          _settings;
    private readonly IHostEnvironment     _env;
    private readonly ILogger<CustomerOtpSendHandler> _logger;

    public CustomerOtpSendHandler(
        LaundryGharDbContext db,
        IOtpSender sender,
        IOptions<OtpSettings> otpOptions,
        IHostEnvironment env,
        ILogger<CustomerOtpSendHandler> logger)
    {
        _db       = db;
        _sender   = sender;
        _settings = otpOptions.Value;
        _env      = env;
        _logger   = logger;
    }

    public async Task<OtpSentResponse> Handle(CustomerOtpSendCommand cmd, CancellationToken ct)
    {
        // H1: Both the cooldown check AND the invalidation query are scoped to
        // (phone, brand) — a send for Brand B must not affect Brand A's pending OTP
        // for the same phone, and the resend cooldown is per-(phone, brand) pair.
        var cooldownCutoff = DateTimeOffset.UtcNow.AddSeconds(-_settings.ResendCooldownSeconds);
        var recent = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.Login
                     && o.ReferenceId    == cmd.ResolvedBrandId   // H1: brand-scoped
                     && o.ReferenceType  == "brand"               // H1: brand-scoped
                     && o.VerifiedAt     == null
                     && o.CreatedAt      > cooldownCutoff)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (recent is not null)
        {
            var wait = (int)Math.Ceiling(
                (recent.CreatedAt.AddSeconds(_settings.ResendCooldownSeconds) - DateTimeOffset.UtcNow)
                .TotalSeconds);
            throw new laundryghar.Utilities.Exceptions.ValidationException(
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
                     && o.ReferenceId    == cmd.ResolvedBrandId   // H1: brand-scoped
                     && o.ReferenceType  == "brand"               // H1: brand-scoped
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        foreach (var old in existing)
            old.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Optionally associate with existing customer
        var customerId = await _db.Customers
            .Where(c => c.BrandId == cmd.ResolvedBrandId && c.PhoneE164 == cmd.Phone)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        var plainCode = GenerateNumericCode(CodeLength);
        var codeHash  = HashOtp(plainCode);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.TtlMinutes);

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        // ReferenceId stores the brand context so verify can resolve the correct brand.
        // ReferenceType = "brand"; ReferenceId = brand_id UUID.
        _db.OtpCodes.Add(new OtpCode
        {
            Id             = Guid.NewGuid(),
            Purpose        = OtpPurpose.Login,
            Identifier     = cmd.Phone,
            IdentifierType = "phone",
            CodeHash       = codeHash,
            CustomerId     = customerId,
            ReferenceId    = cmd.ResolvedBrandId,
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
            logPhone, cmd.ResolvedBrandId, expiresAt);

        await _sender.SendAsync(cmd.Phone, "phone", plainCode, OtpPurpose.Login, ct);

        return new OtpSentResponse("OTP sent successfully.", expiresAt);
    }

    private static string GenerateNumericCode(int length)
    {
        var max    = (int)Math.Pow(10, length);
        var random = RandomNumberGenerator.GetInt32(max);
        return random.ToString().PadLeft(length, '0');
    }

    private static string HashOtp(string code)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// L2: Masks all but the country code prefix and last 4 digits.
    /// E.g. +919876543210 → +91XXXXXX3210
    /// Falls back to a fully masked string for non-E.164 values.
    /// </summary>
    internal static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone)) return "****";

        // Keep country code ('+' + 1-3 digits) and last 4 digits; mask the middle
        // E.164: +[1-3 digit cc][subscriber]. Minimum length with cc=1 is 7 chars.
        if (phone.Length > 6 && phone.StartsWith('+'))
        {
            // Find where subscriber number starts: skip '+' then digits until we have up to 3 cc digits
            int ccEnd = 1;
            while (ccEnd < phone.Length && ccEnd <= 3 && char.IsDigit(phone[ccEnd]))
                ccEnd++;

            var prefix  = phone[..ccEnd];             // e.g. "+91"
            var last4   = phone.Length >= 4 ? phone[^4..] : phone;
            var midLen  = phone.Length - ccEnd - last4.Length;
            var masked  = midLen > 0 ? new string('X', midLen) : string.Empty;
            return $"{prefix}{masked}{last4}";
        }

        // Fallback for non-E.164 or very short values
        return phone.Length <= 4 ? "****" : $"****{phone[^4..]}";
    }
}
