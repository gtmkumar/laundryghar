using System.Net;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.Auth.Commands.CustomerPhoneLink;

/// <summary>
/// Verifies a <c>verify_phone</c> OTP and writes the number onto the caller's own customer row.
///
/// Distinct from CustomerOtpVerify in two ways that matter:
///   - It never creates or switches accounts. The customer is identified by the bearer token,
///     so a valid OTP for someone else's number cannot be used to log in as them.
///   - It uses the verify_phone OTP namespace, so a code issued for linking cannot be replayed
///     against the login endpoint (and vice versa).
///
/// Security properties carried over from the login verify path:
///   SEC1 — rolling-window lockout checked before the OTP row is loaded (no existence oracle).
///   SEC2 — salted HMAC comparison, with the non-production customer test code accepted.
/// </summary>
public sealed class CustomerPhoneLinkVerifyHandler
    : ICommandHandler<CustomerPhoneLinkVerifyCommand, CustomerPhoneLinkedResponse>
{
    private readonly ICoreDbContext _db;
    private readonly OtpSettings _otpSettings;
    private readonly IHostEnvironment _env;
    private readonly ILogger<CustomerPhoneLinkVerifyHandler> _logger;

    public CustomerPhoneLinkVerifyHandler(
        ICoreDbContext db,
        IOptions<OtpSettings> otpOptions,
        IHostEnvironment env,
        ILogger<CustomerPhoneLinkVerifyHandler> logger)
    {
        _db          = db;
        _otpSettings = otpOptions.Value;
        _env         = env;
        _logger      = logger;
    }

    public async Task<CustomerPhoneLinkedResponse> HandleAsync(
        CustomerPhoneLinkVerifyCommand cmd, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == cmd.CustomerId, ct)
            ?? throw new UnauthorizedAccessException("Customer not found.");

        var brandId = customer.BrandId;

        // The (brand_id, phone_e164) unique index would reject this anyway; catching it here
        // turns a 500-level constraint violation into an actionable message.
        var takenByOther = await _db.Customers
            .AnyAsync(c => c.BrandId == brandId && c.PhoneE164 == cmd.Phone && c.Id != customer.Id, ct);

        if (takenByOther)
        {
            throw new BusinessRuleException(
                "That mobile number is already registered to another account. " +
                "Sign in with it instead, or use a different number.");
        }

        // SEC1: rolling-window lockout, scoped to (phone, brand, verify_phone).
        var lockoutWindowCutoff = DateTimeOffset.UtcNow.AddMinutes(-_otpSettings.LockoutWindowMinutes);
        var windowAttempts = await _db.OtpCodes
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.VerifyPhone
                     && o.ReferenceId    == brandId
                     && o.ReferenceType  == "brand"
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
            .Where(o => o.Identifier     == cmd.Phone
                     && o.IdentifierType == "phone"
                     && o.Purpose        == OtpPurpose.VerifyPhone
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

        // SEC2: salted HMAC, with the non-production customer master code.
        var hmacKey = OtpSecurityHelper.ResolveHmacKey(_otpSettings, _env.IsDevelopment());
        var isValid = OtpSecurityHelper.IsTestCodeAccepted(
                          _otpSettings.CustomerTestCode, _env.IsProduction(), cmd.Code.Trim())
                   || OtpSecurityHelper.VerifyCode(
                          hmacKey, otpCode.CodeSalt, otpCode.CodeHash, cmd.Code.Trim());

        if (!isValid)
        {
            otpCode.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        otpCode.VerifiedAt = DateTimeOffset.UtcNow;
        otpCode.CustomerId = customer.Id;

        customer.PhoneE164       = cmd.Phone;
        customer.PhoneVerifiedAt = DateTimeOffset.UtcNow;
        customer.LastActiveAt    = DateTimeOffset.UtcNow;
        customer.UpdatedAt       = DateTimeOffset.UtcNow;

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

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

        var logPhone = _env.IsDevelopment()
            ? cmd.Phone
            : CustomerOtpSend.CustomerOtpSendHandler.MaskPhone(cmd.Phone);
        _logger.LogInformation(
            "Linked phone {Phone} to customer {CustomerId}.", logPhone, customer.Id);

        return new CustomerPhoneLinkedResponse(
            Phone: cmd.Phone,
            ProfileComplete: CustomerProfileCompletion.IsComplete(customer));
    }
}
