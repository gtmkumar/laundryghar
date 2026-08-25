using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Auth.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CustomerEntity = laundryghar.SharedDataModel.Entities.CustomerCatalog.Customer;

namespace core.Application.Identity.Auth.Commands.CustomerGoogleSignIn;

/// <summary>
/// Verifies a Google ID token and signs the customer in, creating the account on first use.
///
/// Account resolution, in order:
///   1. An existing google identity link for this (brand, google sub) — the normal repeat login.
///   2. A customer with the same VERIFIED email — an existing phone-registered customer who
///      is signing in with Google for the first time. Per the onboarding rules this is a
///      sign-IN, not a sign-up: we link the provider to the account they already have rather
///      than creating a duplicate.
///   3. Otherwise create a new customer from the Google profile: name + email, no phone.
///
/// Sign-up asks for nothing else. The phone number is offered afterwards and is skippable
/// (see CustomerPhoneLinkVerify), and the caller lands on the dashboard either way.
///
/// Security notes:
///   - An unverified Google email is rejected outright. Matching an existing account on an
///     unverified address would let anyone who can create a Google account with someone
///     else's address take over that account.
///   - The provider subject (`sub`), not the email, is the durable join key: Google account
///     emails can change, subjects cannot.
/// </summary>
public sealed class CustomerGoogleSignInHandler
    : ICommandHandler<CustomerGoogleSignInCommand, CustomerTokenResponse>
{
    private readonly ICoreDbContext _db;
    private readonly IGoogleIdTokenVerifier _google;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly JwtSettings _jwtSettings;
    private readonly IConfiguration _config;
    private readonly ILogger<CustomerGoogleSignInHandler> _logger;

    public CustomerGoogleSignInHandler(
        ICoreDbContext db,
        IGoogleIdTokenVerifier google,
        IJwtTokenService jwt,
        IRefreshTokenRepository refreshTokens,
        IOptions<JwtSettings> jwtOptions,
        IConfiguration config,
        ILogger<CustomerGoogleSignInHandler> logger)
    {
        _db            = db;
        _google        = google;
        _jwt           = jwt;
        _refreshTokens = refreshTokens;
        _jwtSettings   = jwtOptions.Value;
        _config        = config;
        _logger        = logger;
    }

    public async Task<CustomerTokenResponse> HandleAsync(
        CustomerGoogleSignInCommand cmd, CancellationToken ct)
    {
        // Throws UnauthorizedAccessException on any verification failure.
        var identity = await _google.VerifyAsync(cmd.IdToken, ct);

        if (string.IsNullOrWhiteSpace(identity.Email) || !identity.EmailVerified)
        {
            _logger.LogWarning(
                "Google sign-in rejected for subject {Subject}: email missing or unverified.",
                identity.Subject);
            throw new UnauthorizedAccessException(
                "Your Google account has no verified email address, so it cannot be used to sign in.");
        }

        var brandId = await CustomerBrandResolver.ResolveAsync(
            _db, _config, cmd.RawHeaderBrandId, cmd.BodyBrandCode, ct);

        var email = identity.Email.Trim();

        // ── 1. Existing provider link ─────────────────────────────────────────
        var link = await _db.CustomerIdentities
            .FirstOrDefaultAsync(
                i => i.BrandId == brandId
                  && i.Provider == CustomerIdentityProvider.Google
                  && i.ProviderUid == identity.Subject,
                ct);

        CustomerEntity? customer = null;
        var isNew = false;

        if (link is not null)
        {
            customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == link.CustomerId, ct);

            // The link's customer row was hard-deleted (or filtered out as soft-deleted).
            // Drop the orphan link and fall through to match-by-email / create.
            if (customer is null)
            {
                _logger.LogWarning(
                    "Google identity {IdentityId} points at missing customer {CustomerId}; recreating.",
                    link.Id, link.CustomerId);
                _db.CustomerIdentities.Remove(link);
                link = null;
            }
        }

        // ── 2. Match an existing account by verified email ────────────────────
        if (customer is null)
        {
            customer = await _db.Customers
                .FirstOrDefaultAsync(c => c.BrandId == brandId && c.Email == email, ct);
        }

        // ── 3. Create the account ─────────────────────────────────────────────
        if (customer is null)
        {
            customer = await CreateCustomerAsync(brandId, identity, email, ct);
            isNew = true;
        }
        else
        {
            ApplyGoogleProfile(customer, identity, email);
        }

        if (customer.Status is "blocked" or "deleted")
        {
            _logger.LogWarning("Google sign-in blocked for customer {CustomerId} (status {Status}).",
                customer.Id, customer.Status);
            throw new UnauthorizedAccessException("This account is not active.");
        }

        customer.LastActiveAt = DateTimeOffset.UtcNow;
        customer.UpdatedAt    = DateTimeOffset.UtcNow;

        // ── Upsert the provider link ──────────────────────────────────────────
        if (link is null)
        {
            _db.CustomerIdentities.Add(new CustomerIdentity
            {
                Id            = Guid.NewGuid(),
                CustomerId    = customer.Id,
                BrandId       = brandId,
                Provider      = CustomerIdentityProvider.Google,
                ProviderUid   = identity.Subject,
                Email         = email,
                EmailVerified = identity.EmailVerified,
                DisplayName   = identity.Name,
                AvatarUrl     = identity.PictureUrl,
                LastLoginAt   = DateTimeOffset.UtcNow,
                CreatedAt     = DateTimeOffset.UtcNow,
                UpdatedAt     = DateTimeOffset.UtcNow,
                Version       = 1
            });
        }
        else
        {
            link.Email         = email;
            link.EmailVerified = identity.EmailVerified;
            link.DisplayName   = identity.Name ?? link.DisplayName;
            link.AvatarUrl     = identity.PictureUrl ?? link.AvatarUrl;
            link.LastLoginAt   = DateTimeOffset.UtcNow;
            link.UpdatedAt     = DateTimeOffset.UtcNow;
            link.Version++;
        }

        await _db.SaveChangesAsync(ct);

        return await CustomerSessionIssuer.IssueAsync(
            _db, _jwt, _refreshTokens, _jwtSettings,
            customer,
            authMethod:    AuthMethod.OAuth,
            identifier:    email,
            ipAddressRaw:  cmd.IpAddress,
            userAgent:     cmd.UserAgent,
            isNewCustomer: isNew,
            ct:            ct);
    }

    private async Task<CustomerEntity> CreateCustomerAsync(
        Guid brandId, GoogleIdentity identity, string email, CancellationToken ct)
    {
        // A duplicate email under the same brand can only happen if two sign-ins race; the
        // DB has no unique index on (brand_id, email), so guard by re-checking is pointless —
        // step 2 above already looked. Worst case two rows exist and the provider link
        // (which IS unique) pins the winner.
        var customer = new CustomerEntity
        {
            Id                   = Guid.NewGuid(),
            BrandId              = brandId,
            CustomerCode         = await CustomerSessionIssuer.GenerateUniqueCodeAsync(_db, brandId, ct),
            PhoneE164            = null,   // Google-first: no phone until the customer links one
            Email                = email,
            EmailVerifiedAt      = DateTimeOffset.UtcNow,
            FirstName            = identity.GivenName,
            LastName             = identity.FamilyName,
            DisplayName          = identity.Name,
            AvatarUrl            = identity.PictureUrl,
            Locale               = "en-IN",
            Timezone             = "Asia/Kolkata",
            Status               = "active",
            Metadata             = "{}",
            Tags                 = [],
            LifetimeOrders       = 0,
            LifetimeSpend        = 0,
            LoyaltyPointsBalance = 0,
            WalletBalance        = 0,
            // L1 (DPDP Act 2023): marketing-class opt-ins require affirmative consent.
            MarketingOptIn       = false,
            SmsOptIn             = false,
            WhatsappOptIn        = false,
            EmailOptIn           = false,
            PushOptIn            = false,
            CreatedAt            = DateTimeOffset.UtcNow,
            UpdatedAt            = DateTimeOffset.UtcNow,
            Version              = 1
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created customer {CustomerId} from Google sign-in under brand {BrandId}.",
            customer.Id, brandId);

        return customer;
    }

    /// <summary>
    /// Fills in profile fields Google can supply, without overwriting anything the customer
    /// has already set themselves — a name edited in the app must survive the next sign-in.
    /// </summary>
    private static void ApplyGoogleProfile(CustomerEntity customer, GoogleIdentity identity, string email)
    {
        customer.Email ??= email;
        customer.FirstName ??= identity.GivenName;
        customer.LastName ??= identity.FamilyName;
        customer.DisplayName ??= identity.Name;
        customer.AvatarUrl ??= identity.PictureUrl;

        // Google asserted this mailbox; record the verification if we had not already.
        if (customer.EmailVerifiedAt is null
            && string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase))
            customer.EmailVerifiedAt = DateTimeOffset.UtcNow;
    }
}
