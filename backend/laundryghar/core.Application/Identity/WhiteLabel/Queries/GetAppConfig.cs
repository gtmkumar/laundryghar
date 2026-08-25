using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;

namespace core.Application.Identity.WhiteLabel.Queries;

public sealed record GetAppConfigQuery(Guid BrandId, string App) : IQuery<AppConfigDto?>;

/// <summary>
/// Everything an Expo build needs to become THIS provider's app — exactly the fields
/// <c>app.config.ts</c> hardcodes today (§4 tier T3, §11 P4).
/// </summary>
/// <param name="Slug">Also the EAS project slug, so one value drives naming and OTA channels.</param>
/// <param name="BundleIdentifier">iOS. Reverse-DNS from the brand code, which is already unique.</param>
/// <param name="Package">Android. Same string — keeping them identical is what makes the two stores
/// answerable from one config instead of two.</param>
public sealed record AppConfigDto(
    string App,
    string Name,
    string Slug,
    string Scheme,
    string BundleIdentifier,
    string Package,
    string PrimaryColor,
    string? IconUrl,
    string? SplashUrl,
    string? PrimaryDomain,
    string SupportEmail,
    string? SupportPhone);

/// <summary>
/// The half of §4's white-label app factory that does not depend on OQ-7.
///
/// <para><b>What is still blocked.</b> §4 calls T3 a "one-time fee" but never says who submits to the
/// App Store and Play Store. If we submit on the provider's behalf, the factory needs to hold their
/// developer-account credentials — which is not something to build without an explicit decision. If
/// the provider submits, it needs a config bundle and instructions. Very different builds.</para>
///
/// <para><b>What is not blocked.</b> Either way the app needs the same per-brand values, and today
/// they are hardcoded in <c>customer-mobile/app.config.ts</c> and <c>rider-mobile/app.config.ts</c>.
/// Deriving them from the brand is the shared prerequisite of both answers, so it is built.</para>
/// </summary>
public sealed class GetAppConfigQueryHandler : IQueryHandler<GetAppConfigQuery, AppConfigDto?>
{
    /// <summary>The apps a provider can be given. Named rather than free-form so a typo produces a
    /// 404 instead of a config for an app that does not exist.</summary>
    public static readonly string[] KnownApps = ["customer", "rider"];

    /// <summary>Reverse-DNS root. Ours, not the provider's — the bundle id has to be globally unique
    /// and we are the ones who can guarantee that.</summary>
    public const string BundleRoot = "com.laundryghar";

    private readonly IOnboardingFactsStore _brands;
    public GetAppConfigQueryHandler(IOnboardingFactsStore brands) => _brands = brands;

    public async Task<AppConfigDto?> HandleAsync(GetAppConfigQuery query, CancellationToken ct)
    {
        var app = query.App?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!KnownApps.Contains(app)) return null;

        // Through the SECURITY DEFINER lookup, never EF. tenancy_org.brands is admin-only under
        // RLS, so an owner reading their OWN brand's name gets zero rows — this endpoint answered
        // 404 to the person whose app it is. The brands-RLS trap, for the fifth time.
        var brand = await _brands.GetAppIdentityAsync(query.BrandId, ct);
        if (brand is null) return null;

        var domain = brand.PrimaryDomain;

        // TWO different sanitisations, because the platforms disagree about what a name may contain.
        //
        //   slugRoot  — hyphens allowed. Used for the Expo slug and the URL scheme, both of which
        //               accept them, and where the hyphen keeps a multi-word brand readable.
        //   idRoot    — alphanumerics ONLY. An Android package name's segments must be valid Java
        //               identifiers: `com.laundryghar.lg-main.customer` is a perfectly reasonable
        //               string and an INVALID package, which fails at build time rather than here.
        //
        // Neither can be changed after the first store submission, so both err toward boring.
        var slugRoot = Slug(brand.Code);
        var idRoot = IdentifierSegment(brand.Code);

        return new AppConfigDto(
            App: app,
            Name: brand.Name,
            Slug: $"{slugRoot}-{app}",
            // The deep-link scheme. Per brand AND per app, because two apps from the same provider
            // installed side by side would otherwise fight over the same links.
            // Hyphenated, not concatenated: `lg-main` + `customer` and `lg` + `maincustomer` would
            // otherwise produce the same scheme and two brands would fight over each other's links.
            Scheme: $"{slugRoot}-{app}",
            BundleIdentifier: $"{BundleRoot}.{idRoot}.{app}",
            Package: $"{BundleRoot}.{idRoot}.{app}",
            PrimaryColor: string.IsNullOrWhiteSpace(brand.PrimaryColor) ? "#4A552A" : brand.PrimaryColor,
            IconUrl: brand.LogoUrl,
            SplashUrl: brand.LogoUrl,
            PrimaryDomain: domain,
            SupportEmail: brand.SupportEmail ?? string.Empty,
            SupportPhone: brand.SupportPhone);
    }

    /// <summary>Internal for testing. Store identifiers cannot be changed after first submission, so
    /// this errs toward boring: lower-case letters and digits, runs collapsed, edges trimmed.</summary>
    /// <summary>
    /// Internal for testing. An Android package segment must be a valid Java identifier — letters
    /// and digits only, never starting with a digit — so this strips rather than collapses. iOS
    /// tolerates hyphens; Android does not, and one string has to satisfy both.
    /// </summary>
    internal static string IdentifierSegment(string value)
    {
        var kept = new string((value ?? string.Empty).ToLowerInvariant()
            .Where(char.IsAsciiLetterOrDigit).ToArray());

        if (kept.Length == 0 || char.IsAsciiDigit(kept[0])) kept = "b" + kept;

        return kept;
    }

    internal static string Slug(string value)
    {
        var chars = (value ?? string.Empty).ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');

        while (slug.Contains("--")) slug = slug.Replace("--", "-");

        // A bundle identifier segment cannot start with a digit on iOS, and cannot be empty.
        if (slug.Length == 0 || char.IsAsciiDigit(slug[0])) slug = "b" + slug;

        return slug;
    }
}
