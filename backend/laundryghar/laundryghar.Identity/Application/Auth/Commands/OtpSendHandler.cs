using System.Security.Cryptography;
using laundryghar.Identity.Application.Auth.Dtos;
using laundryghar.Identity.Infrastructure.Auth;
using MediatR;
using Microsoft.Extensions.Options;

namespace laundryghar.Identity.Application.Auth.Commands;

/// <summary>
/// Generates a 6-digit OTP, stores its SHA-256 hash in otp_codes,
/// then dispatches via IOtpSender.
/// L6: TTL/MaxAttempts read from IOptions&lt;OtpSettings&gt; (Otp config section).
/// C5: Per-identifier cooldown enforced before issuing a new OTP.
/// </summary>
public sealed class OtpSendHandler : IRequestHandler<OtpSendCommand, OtpSentResponse>
{
    private const int CodeLength = 6;

    private readonly LaundryGharDbContext _db;
    private readonly IOtpSender           _sender;
    private readonly OtpSettings          _settings;
    private readonly ILogger<OtpSendHandler> _logger;

    public OtpSendHandler(
        LaundryGharDbContext db,
        IOtpSender sender,
        IOptions<OtpSettings> otpOptions,
        ILogger<OtpSendHandler> logger)
    {
        _db       = db;
        _sender   = sender;
        _settings = otpOptions.Value;
        _logger   = logger;
    }

    public async Task<OtpSentResponse> Handle(OtpSendCommand cmd, CancellationToken ct)
    {
        // C5: Per-identifier cooldown — reject if an OTP was issued within the cooldown window
        var cooldownCutoff = DateTimeOffset.UtcNow.AddSeconds(-_settings.ResendCooldownSeconds);
        var recentOtp = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Identifier
                     && o.IdentifierType == cmd.IdentifierType
                     && o.Purpose        == cmd.Purpose
                     && o.VerifiedAt     == null
                     && o.CreatedAt      > cooldownCutoff)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (recentOtp is not null)
        {
            var waitSeconds = (int)Math.Ceiling(
                (recentOtp.CreatedAt.AddSeconds(_settings.ResendCooldownSeconds) - DateTimeOffset.UtcNow).TotalSeconds);
            throw new laundryghar.Utilities.Exceptions.ValidationException(
                new Dictionary<string, string[]>
                {
                    ["identifier"] = [$"Please wait {waitSeconds} seconds before requesting a new OTP."]
                });
        }

        // Invalidate any existing un-verified OTPs for this identifier+purpose
        var existing = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Identifier
                     && o.Purpose        == cmd.Purpose
                     && o.IdentifierType == cmd.IdentifierType
                     && o.VerifiedAt     == null
                     && o.ExpiresAt      > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

        // Expire old codes (no explicit "invalidated" state in schema; expiring is idiomatic)
        foreach (var old in existing)
            old.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Generate OTP
        var plainCode = GenerateNumericCode(CodeLength);
        var codeHash  = HashOtp(plainCode);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.TtlMinutes);

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        var userId = await _db.Users
            .Where(u => u.Email == cmd.Identifier || u.PhoneE164 == cmd.Identifier)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        _db.OtpCodes.Add(new OtpCode
        {
            Id             = Guid.NewGuid(),
            Purpose        = cmd.Purpose,
            Identifier     = cmd.Identifier,
            IdentifierType = cmd.IdentifierType,
            CodeHash       = codeHash,
            UserId         = userId,
            Attempts       = 0,
            MaxAttempts    = (short)_settings.MaxAttempts,
            ExpiresAt      = expiresAt,
            IpAddress      = ipAddress,
            UserAgent      = cmd.UserAgent,
            CreatedAt      = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        await _sender.SendAsync(cmd.Identifier, cmd.IdentifierType, plainCode, cmd.Purpose, ct);

        return new OtpSentResponse(
            Message:   "OTP sent successfully.",
            ExpiresAt: expiresAt);
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
}
