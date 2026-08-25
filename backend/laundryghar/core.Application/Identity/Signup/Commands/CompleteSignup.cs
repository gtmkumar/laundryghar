using core.Application.Common;
using core.Application.Common.Interfaces;
using core.Application.Identity.Auth.Common;
using core.Application.Identity.Signup.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.Signup.Commands;

public sealed record CompleteSignupCommand(SignupCompleteRequest Request, string? IpAddress)
    : ICommand<SignupCompleteResponse>;

/// <summary>
/// PLATFORM_STRATEGY.md §9, step 2: the phone is proven and the provider's business comes into
/// existence — brand, owner, plan and a seeded catalogue, in one transaction.
///
/// <para>This is the entry point of the entire funnel, and until now it did not exist: a brand could
/// only be created by a platform admin through the back office. §7 promises "create provider → pick
/// vertical template → pick plan → live in minutes"; this is what makes that self-serve.</para>
///
/// <para><b>Why one transaction.</b> A brand with no owner is unreachable; an owner with no features
/// logs into an empty console (entitlement enforcement is ON); a brand with no trial is unbillable.
/// A partial signup is worse than a failed one, because a failed one can simply be retried —
/// so everything commits together or nothing does.</para>
///
/// <para><b>Anonymous, and therefore hostile-input territory.</b> The caller has no identity, so:
/// the phone must be proven by OTP before anything is written; the brand code is generated, never
/// accepted from the request (a caller must not be able to choose their own tenant identifier or
/// squat someone else's); and the template must be one that is actually offered.</para>
/// </summary>
public class CompleteSignupCommandHandler : ICommandHandler<CompleteSignupCommand, SignupCompleteResponse>
{
    /// <summary>§9: "pick vertical template + plan (trial N days)". Fixed at 14 — §9 names a trial
    /// but never its length, so this is a placeholder to be confirmed alongside pricing (OQ-4).</summary>
    public const int TrialDays = 14;

    private readonly ICoreDbContext _db;
    private readonly OtpSettings _otpSettings;
    private readonly IHostEnvironment _env;

    public CompleteSignupCommandHandler(
        ICoreDbContext db, IOptions<OtpSettings> otpOptions, IHostEnvironment env)
    {
        _db = db;
        _otpSettings = otpOptions.Value;
        _env = env;
    }

    public async Task<SignupCompleteResponse> HandleAsync(CompleteSignupCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var phone = req.PhoneE164?.Trim() ?? string.Empty;
        var businessName = req.BusinessName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(businessName))
            throw Invalid("businessName", "Tell us your business name.");
        if (string.IsNullOrWhiteSpace(phone))
            throw Invalid("phoneE164", "A phone number is required.");

        // ── 1. The template must be one we actually offer ────────────────────────────────────
        // is_public matters: a template whose fulfilment mode has no strategy would let someone sign
        // up for a business that cannot take an order.
        var template = await _db.VerticalTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == req.TemplateKey && t.IsPublic, ct)
            ?? throw Invalid("templateKey", "Choose one of the available business types.");

        // ── 2. Prove the phone BEFORE writing anything ───────────────────────────────────────
        await ConsumeOtpAsync(phone, req.Code ?? string.Empty, ct);

        // ── 3. Refuse a second account on the same phone ─────────────────────────────────────
        // The phone is the owner's identity; letting it own two brands would make "which business am
        // I signing into?" ambiguous at every future login.
        if (await _db.Users.AnyAsync(u => u.PhoneE164 == phone && u.DeletedAt == null, ct))
            throw Invalid("phoneE164", "An account already exists for this number. Sign in instead.");

        var now = DateTimeOffset.UtcNow;

        // Everything below is ONE transaction with several SaveChanges inside it. The staging is
        // forced by EF: `brand_feature`, the membership and the catalogue all reference `brands` and
        // `users` through natural-key FKs with no navigation property, so EF cannot infer that the
        // parents must be inserted first and orders them arbitrarily —
        //     insert or update on table "brand_feature" violates foreign key constraint
        //         "brand_feature_brand_id_fkey"
        // Saving in stages fixes the order; the transaction keeps it atomic, because a brand that
        // exists with no owner, no features and no plan is worse than a signup that simply failed.
        return await _db.ExecuteInTransactionAsync(async innerCt =>
        {

        // ── 4. The brand ─────────────────────────────────────────────────────────────────────
        var platformId = await _db.Platforms.AsNoTracking()
            .OrderBy(p => p.CreatedAt).Select(p => p.Id).FirstOrDefaultAsync(innerCt);
        if (platformId == Guid.Empty)
            throw new BusinessRuleException("No platform is configured to host new providers.");

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            // Generated, never client-supplied — see the class comment.
            Code = await GenerateBrandCodeAsync(businessName, innerCt),
            Name = businessName,
            LegalName = businessName,
            VerticalKey = template.VerticalKey,
            CurrencyCode = "INR",
            CountryCode = "IN",
            Timezone = "Asia/Kolkata",
            LocaleDefault = "en-IN",
            LocalesEnabled = ["en-IN", "hi-IN"],
            SupportPhone = phone,
            SupportEmail = req.Email?.Trim(),
            Config = GstinConfig(req.Gstin),
            // 'active', not 'trialing': brands.status is the SUSPENSION axis that §9's login-only gate
            // reads, and a trialing provider must be able to operate. The trial lives on the
            // subscription, which is where billing looks for it.
            Status = "active",
            CreatedAt = now, UpdatedAt = now, Version = 1,
        };
        _db.Brands.Add(brand);

        // ── 5. The owner ─────────────────────────────────────────────────────────────────────
        var owner = new User
        {
            Id = Guid.NewGuid(),
            PhoneE164 = phone,
            Email = req.Email?.Trim(),
            // Proven moments ago by the OTP above.
            PhoneVerifiedAt = now,
            UserType = UserType.Staff,
            Status = "active",
            Locale = "en-IN",
            Timezone = "Asia/Kolkata",
            CreatedAt = now, UpdatedAt = now, Version = 1, PermVersion = 0,
        };
        _db.Users.Add(owner);

        // Stage 1: the two parents everything else points at.
        await _db.SaveChangesAsync(innerCt);

        // brand_admin is the closest shipped role to §6's "Owner" — full control of one brand. The
        // named Owner preset arrives with T-24; using it here would mean inventing a role.
        var ownerRole = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Code == "brand_admin" && r.DeletedAt == null, innerCt)
            ?? throw new BusinessRuleException("The brand_admin role is missing; cannot create an owner.");

        _db.UserScopeMemberships.Add(new UserScopeMembership
        {
            Id = Guid.NewGuid(), UserId = owner.Id, RoleId = ownerRole.Id,
            ScopeType = ScopeType.Brand, ScopeId = brand.Id,
            IsPrimary = true, GrantedAt = now, Metadata = "{}", CreatedAt = now,
        });

        // ── 5b. The provider's own operating entity ──────────────────────────────────────────
        // §9's wizard opens with "add your first location", and a store REQUIRES a franchise
        // (tenancy_org.stores.franchise_id). A brand-new provider had none, so the very first step
        // of onboarding was impossible — found by driving the wizard against a freshly signed-up
        // account.
        //
        // What is created is not a franchisee. It is the provider operating their own business:
        // zero royalty, zero marketing fee, owned by the person who just signed up. A provider who
        // later franchises to someone else adds real franchises alongside this one; a provider who
        // never does simply never thinks about it.
        _db.Franchises.Add(new Franchise
        {
            Id = Guid.NewGuid(), BrandId = brand.Id, OwnerUserId = owner.Id,
            Code = "OWN", LegalName = businessName, DisplayName = businessName,
            ContactPhone = phone, ContactEmail = req.Email?.Trim(),
            BillingAddress = "{}", OperationalAddress = null,
            RoyaltyPercent = 0m, MarketingFeePercent = 0m,
            OnboardingStatus = "active", OnboardedAt = now,   // the vocabulary this column actually allows
            Config = "{}", Metadata = "{}", Status = "active", Version = 1,
            CreatedAt = now, UpdatedAt = now, CreatedBy = owner.Id, UpdatedBy = owner.Id,
        });

        // ── 6. Features + catalogue from the template ────────────────────────────────────────
        var provisioned = await new TemplateProvisioner(_db).ApplyAsync(brand.Id, template, owner.Id, innerCt);

        // ── 7. The trial ─────────────────────────────────────────────────────────────────────
        DateTimeOffset? trialEndsAt = null;
        if (template.DefaultBundleCode is { } bundleCode)
        {
            var bundle = await _db.ModuleBundles.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Code == bundleCode, innerCt);

            if (bundle is not null)
            {
                trialEndsAt = now.AddDays(TrialDays);
                _db.BrandPlatformSubscriptions.Add(new BrandPlatformSubscription
                {
                    Id = Guid.NewGuid(), BrandId = brand.Id,
                    BundleCode = bundle.Code, PlanName = bundle.Name,
                    Price = bundle.Price ?? 0m,
                    BillingInterval = bundle.BillingInterval ?? "monthly",
                    CurrencyCode = bundle.CurrencyCode ?? "INR",
                    // 'trialing' — nothing is owed until the trial ends, so no invoice is issued here.
                    Status = "trialing",
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = trialEndsAt.Value,
                    NextBillingAt = trialEndsAt.Value,
                    AutoRenew = true,
                    CreatedAt = now, UpdatedAt = now, CreatedBy = owner.Id, UpdatedBy = owner.Id,
                });
            }
        }

        // Stage 2: membership, features, catalogue and the trial subscription.
        await _db.SaveChangesAsync(innerCt);

        return new SignupCompleteResponse(
            brand.Id, brand.Code, brand.Name, brand.VerticalKey, template.Key,
            provisioned.BundleCode, brand.Status, trialEndsAt,
            provisioned.CategoriesCreated, provisioned.ItemsCreated);

        }, ct);
    }

    /// <summary>
    /// Verifies and consumes the signup OTP. Mirrors <c>OtpVerifyHandler</c>'s crypto exactly
    /// (salted HMAC, attempt counting, non-production test code) but not its user lookup: at signup
    /// there is no user yet — proving the phone is what earns the right to create one.
    /// </summary>
    private async Task ConsumeOtpAsync(string phone, string code, CancellationToken ct)
    {
        var otp = await _db.OtpCodes
            .Where(o => o.Identifier == phone
                     && o.IdentifierType == "phone"
                     && o.Purpose == OtpPurpose.Signup
                     && o.VerifiedAt == null
                     && o.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otp is null)
            throw new UnauthorizedAccessException("OTP not found or expired.");

        if (otp.Attempts >= otp.MaxAttempts)
            throw new UnauthorizedAccessException("Maximum OTP attempts exceeded.");

        var hmacKey = OtpSecurityHelper.ResolveHmacKey(_otpSettings, _env.IsDevelopment());
        var isValid = OtpSecurityHelper.IsTestCodeAccepted(_otpSettings.TestCode, _env.IsProduction(), code.Trim())
                   || OtpSecurityHelper.VerifyCode(hmacKey, otp.CodeSalt, otp.CodeHash, code.Trim());

        if (!isValid)
        {
            otp.Attempts++;
            await _db.SaveChangesAsync(ct);
            throw new UnauthorizedAccessException("Invalid OTP.");
        }

        otp.VerifiedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// A readable, unique brand code derived from the business name. Generated rather than accepted
    /// from the request: the code is the tenant's public identifier (it appears in their sub-domain),
    /// so letting an anonymous caller choose it would allow squatting a competitor's name.
    /// </summary>
    private async Task<string> GenerateBrandCodeAsync(string businessName, CancellationToken ct)
    {
        var baseSlug = new string(businessName.ToUpperInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');

        while (baseSlug.Contains("--")) baseSlug = baseSlug.Replace("--", "-");
        if (baseSlug.Length > 24) baseSlug = baseSlug[..24].Trim('-');
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "PROVIDER";

        if (!await _db.Brands.IgnoreQueryFilters().AnyAsync(b => b.Code == baseSlug, ct))
            return baseSlug;

        // Collisions are expected — two "Sharma Laundry"s is normal — so suffix rather than reject.
        for (var i = 2; i <= 99; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!await _db.Brands.IgnoreQueryFilters().AnyAsync(b => b.Code == candidate, ct))
                return candidate;
        }

        return $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    }

    /// <summary>GSTIN is optional per §9, so it goes in brand config rather than a required column.</summary>
    private static string GstinConfig(string? gstin) =>
        string.IsNullOrWhiteSpace(gstin)
            ? "{}"
            : System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["gstin"] = gstin.Trim() });

    private static ValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
