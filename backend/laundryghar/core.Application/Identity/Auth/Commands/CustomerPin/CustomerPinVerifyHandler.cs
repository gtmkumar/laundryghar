using System.Net;
using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.Auth.Commands.CustomerPin;

/// <summary>
/// Unlocks a returning customer with their PIN instead of another OTP round-trip.
///
/// The PIN is short by design, so the lockout does the security work:
/// <see cref="LockThreshold"/> consecutive failures lock the PIN for
/// <see cref="LockMinutes"/> minutes. That caps an attacker at a handful of guesses per
/// window against a 10 000-value space, and the customer always retains the OTP path as a
/// recovery route — a locked PIN never locks the account.
///
/// Biometric unlock (Face ID / fingerprint) deliberately has no endpoint: the device
/// authenticates locally and then replays its stored refresh token, so the biometric never
/// leaves the handset and the server has no new trust to grant.
/// </summary>
public sealed class CustomerPinVerifyHandler
    : ICommandHandler<CustomerPinVerifyCommand, CustomerTokenResponse>
{
    private const int LockThreshold = 5;
    private const int LockMinutes = 15;

    private readonly ICoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly JwtSettings _jwtSettings;
    private readonly IConfiguration _config;
    private readonly ILogger<CustomerPinVerifyHandler> _logger;

    public CustomerPinVerifyHandler(
        ICoreDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IRefreshTokenRepository refreshTokens,
        IOptions<JwtSettings> jwtOptions,
        IConfiguration config,
        ILogger<CustomerPinVerifyHandler> logger)
    {
        _db            = db;
        _hasher        = hasher;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _jwtSettings   = jwtOptions.Value;
        _config        = config;
        _logger        = logger;
    }

    public async Task<CustomerTokenResponse> HandleAsync(
        CustomerPinVerifyCommand cmd, CancellationToken ct)
    {
        var brandId = await CustomerBrandResolver.ResolveAsync(
            _db, _config, cmd.RawHeaderBrandId, cmd.BodyBrandCode, ct);

        var identifier = cmd.Identifier.Trim();

        var customer = await _db.Customers
            .FirstOrDefaultAsync(
                c => c.BrandId == brandId
                  && (c.PhoneE164 == identifier || c.Email == identifier),
                ct);

        // Uniform failure for "no such account", "no PIN set" and "wrong PIN": distinguishing
        // them would turn this endpoint into an account-enumeration oracle.
        if (customer is null || string.IsNullOrEmpty(customer.PinHash))
        {
            await WriteFailureAsync(null, identifier,
                customer is null ? "customer_not_found" : "pin_not_set", cmd, ct);
            throw new UnauthorizedAccessException("Incorrect PIN.");
        }

        if (customer.PinLockedUntil.HasValue && customer.PinLockedUntil > DateTimeOffset.UtcNow)
        {
            await WriteFailureAsync(customer.Id, identifier, "pin_locked", cmd, ct);
            throw new UnauthorizedAccessException(
                "Too many incorrect PIN attempts. Sign in with an OTP instead, or try again later.");
        }

        if (customer.Status is "blocked" or "deleted")
        {
            await WriteFailureAsync(customer.Id, identifier, "account_suspended", cmd, ct);
            throw new UnauthorizedAccessException("This account is not active.");
        }

        if (!_hasher.Verify(cmd.Pin, customer.PinHash))
        {
            customer.PinFailedAttempts++;
            if (customer.PinFailedAttempts >= LockThreshold)
            {
                customer.PinLockedUntil = DateTimeOffset.UtcNow.AddMinutes(LockMinutes);
                customer.PinFailedAttempts = 0;
                _logger.LogWarning(
                    "Customer {CustomerId} PIN locked after {Threshold} failed attempts.",
                    customer.Id, LockThreshold);
            }
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await WriteFailureAsync(customer.Id, identifier, "invalid_pin", cmd, ct);
            throw new UnauthorizedAccessException("Incorrect PIN.");
        }

        customer.PinFailedAttempts = 0;
        customer.PinLockedUntil    = null;
        customer.LastActiveAt      = DateTimeOffset.UtcNow;
        customer.UpdatedAt         = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await CustomerSessionIssuer.IssueAsync(
            _db, _jwt, _refreshTokens, _jwtSettings,
            customer,
            authMethod:    AuthMethod.Password,   // PIN is a knowledge factor
            identifier:    identifier,
            ipAddressRaw:  cmd.IpAddress,
            userAgent:     cmd.UserAgent,
            isNewCustomer: false,
            ct:            ct);
    }

    private async Task WriteFailureAsync(
        Guid? customerId, string identifier, string reason,
        CustomerPinVerifyCommand cmd, CancellationToken ct)
    {
        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        _db.LoginHistories.Add(new LoginHistory
        {
            Id            = Guid.NewGuid(),
            CustomerId    = customerId,
            Identifier    = identifier,
            AuthMethod    = AuthMethod.Password,
            Success       = false,
            FailureReason = reason,
            IpAddress     = ipAddress,
            UserAgent     = cmd.UserAgent,
            OccurredAt    = DateTimeOffset.UtcNow,
            CreatedAt     = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }
}
