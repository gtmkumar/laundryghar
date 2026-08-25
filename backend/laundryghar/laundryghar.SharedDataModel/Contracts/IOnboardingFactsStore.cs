namespace laundryghar.SharedDataModel.Contracts;

/// <summary>What a provider's account actually contains, which is what each wizard step is judged
/// on. Every field is a count or a fact about real rows — none is a flag someone set.</summary>
/// <param name="OwnFranchiseId">The provider's own operating entity. Surfaced because a store needs
/// a franchise id and `franchises` is an Enterprise feature — without this, a Starter provider is
/// correctly refused the franchise list and so cannot create their first location.</param>
public sealed record OnboardingFacts(
    int Locations, int CatalogItems, int StaffMembers, bool HasGateway, string? PrimaryDomain,
    Guid? OwnFranchiseId);

/// <summary>
/// Reads the facts behind §9's wizard, and puts a brand live on its sub-domain.
///
/// <para>Both go through SECURITY DEFINER functions because the gateway fact and the brand code both
/// live in <c>tenancy_org.brands</c>, which is <c>rls_admin_only</c> — an owner asking how far
/// through setup they are would otherwise read zero rows and be told they had not started.</para>
/// </summary>
public interface IOnboardingFactsStore
{
    Task<OnboardingFacts?> GetAsync(Guid brandId, CancellationToken ct = default);

    /// <summary>Creates (or returns) the brand's <c>&lt;code&gt;.&lt;base&gt;</c> host, verified and
    /// primary. Idempotent, and never displaces a custom domain the provider already went live on.</summary>
    Task<string?> EnsureSubdomainAsync(Guid brandId, string baseDomain, CancellationToken ct = default);

    /// <summary>
    /// The public face of the brand — name, colour, logo, support contact, live domain — for a
    /// white-label build (§4 tier T3).
    ///
    /// <para>Here for the same reason as everything else on this interface: <c>tenancy_org.brands</c>
    /// is admin-only under RLS, so an owner reading their OWN brand's name gets zero rows and the
    /// white-label endpoint answered 404 to the person whose app it is.</para>
    /// </summary>
    Task<BrandAppIdentity?> GetAppIdentityAsync(Guid brandId, CancellationToken ct = default);
}

/// <summary>The public face of a business, for a white-label build (§4 T3).</summary>
public sealed record BrandAppIdentity(
    string Code, string Name, string? PrimaryColor, string? LogoUrl,
    string? SupportEmail, string? SupportPhone, string? PrimaryDomain);
