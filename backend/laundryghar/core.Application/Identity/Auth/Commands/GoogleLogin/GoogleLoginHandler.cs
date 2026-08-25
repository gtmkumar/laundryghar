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
using RefreshTokenEntity = laundryghar.SharedDataModel.Entities.IdentityAccess.RefreshToken;

namespace core.Application.Identity.Auth.Commands.GoogleLogin;

/// <summary>
/// Signs a staff user in with a Google ID token, for the admin and POS consoles.
///
/// The critical difference from the customer flow: this NEVER provisions an account. A staff
/// identity carries roles, scope memberships and permissions, so it must be created through
/// the invite flow by someone authorised to grant them. An unrecognised Google email is
/// rejected — otherwise anyone with a Google account could mint themselves a console login.
///
/// Trust chain: Google asserts the email is verified → we match it to a pre-provisioned user
/// row → that row's roles decide what the token can do. An unverified Google email is refused,
/// since matching on one would let an attacker claim someone else's address.
/// </summary>
public sealed class GoogleLoginHandler : ICommandHandler<GoogleLoginCommand, TokenResponse>
{
    private readonly ICoreDbContext _db;
    private readonly IGoogleIdTokenVerifier _google;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly JwtSettings _jwtSettings;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleLoginHandler> _logger;

    public GoogleLoginHandler(
        ICoreDbContext db,
        IGoogleIdTokenVerifier google,
        IJwtTokenService jwt,
        IRefreshTokenRepository refreshTokens,
        IOptions<JwtSettings> jwtOptions,
        IConfiguration config,
        ILogger<GoogleLoginHandler> logger)
    {
        _db            = db;
        _google        = google;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _jwtSettings   = jwtOptions.Value;
        _config        = config;
        _logger        = logger;
    }

    public async Task<TokenResponse> HandleAsync(GoogleLoginCommand cmd, CancellationToken ct)
    {
        var identity = await _google.VerifyAsync(cmd.IdToken, ct);

        var ipAddress = string.IsNullOrEmpty(cmd.IpAddress) ? null
            : IPAddress.TryParse(cmd.IpAddress, out var ip) ? ip : null;

        if (string.IsNullOrWhiteSpace(identity.Email) || !identity.EmailVerified)
        {
            await WriteLoginHistory(null, identity.Email ?? identity.Subject, false,
                "google_email_unverified", ipAddress, cmd.UserAgent, ct);
            throw new UnauthorizedAccessException(
                "Your Google account has no verified email address, so it cannot be used to sign in.");
        }

        var email = identity.Email.Trim();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            _logger.LogWarning("Google login rejected: no staff account for {Email}.", email);
            await WriteLoginHistory(null, email, false, "user_not_found", ipAddress, cmd.UserAgent, ct);
            throw new UnauthorizedAccessException(
                "No LaundryGhar account is linked to that Google address. Ask an administrator to invite you.");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTimeOffset.UtcNow)
        {
            await WriteLoginHistory(user.Id, email, false, "account_locked", ipAddress, cmd.UserAgent, ct);
            throw new UnauthorizedAccessException($"Account locked until {user.LockedUntil:u}.");
        }

        if (user.Status is UserStatus.Suspended or UserStatus.Deleted)
        {
            await WriteLoginHistory(user.Id, email, false, "account_suspended", ipAddress, cmd.UserAgent, ct);
            throw new UnauthorizedAccessException("Account is not active.");
        }

        // Success — a verified federated sign-in clears the password lockout counters, exactly
        // as a correct password would. The user has proven control of the mailbox that owns
        // this account, which is the same thing a password reset would establish.
        user.FailedAttempts = 0;
        user.LockedUntil    = null;
        user.LastLoginAt    = DateTimeOffset.UtcNow;
        user.LastLoginIp    = ipAddress;
        user.LastActiveAt   = DateTimeOffset.UtcNow;
        if (user.Status == UserStatus.Locked) user.Status = UserStatus.Active;
        user.UpdatedAt      = DateTimeOffset.UtcNow;

        var claims = await ScopeResolver.BuildTokenClaimsAsync(
            _db, user, enforceEntitlement: _config.GetValue<bool>("Entitlement:Enforced"), ct: ct);
        var accessToken = _jwt.CreateAccessToken(claims);

        var rawRefresh = _jwt.GenerateRefreshTokenRaw();
        var tokenHash  = _jwt.HashRefreshToken(rawRefresh);
        var rtId       = Guid.NewGuid();

        var refreshToken = new RefreshTokenEntity
        {
            Id        = rtId,
            UserId    = user.Id,
            TokenHash = tokenHash,
            FamilyId  = rtId,          // root: family_id = own id
            IpAddress = ipAddress,
            UserAgent = cmd.UserAgent,
            IssuedAt  = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshDays),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _db.SaveChangesAsync(ct);
        await _refreshTokens.InsertRootAsync(refreshToken, ct);
        await WriteLoginHistory(user.Id, email, true, null, ipAddress, cmd.UserAgent, ct);

        return new TokenResponse(
            AccessToken:      accessToken,
            RefreshToken:     rawRefresh,
            ExpiresInSeconds: _jwtSettings.AccessMinutes * 60);
    }

    private async Task WriteLoginHistory(
        Guid? userId, string identifier, bool success,
        string? failureReason, IPAddress? ip, string? ua, CancellationToken ct)
    {
        _db.LoginHistories.Add(new LoginHistory
        {
            Id            = Guid.NewGuid(),
            UserId        = userId,
            Identifier    = identifier,
            AuthMethod    = AuthMethod.OAuth,
            Success       = success,
            FailureReason = failureReason,
            IpAddress     = ip,
            UserAgent     = ua,
            OccurredAt    = DateTimeOffset.UtcNow,
            CreatedAt     = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }
}
