using laundryghar.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary><see cref="IOnboardingFactsStore"/> over the two functions in migration 0017.</summary>
public sealed class OnboardingFactsStore : IOnboardingFactsStore
{
    private readonly LaundryGharDbContext _db;

    public OnboardingFactsStore(LaundryGharDbContext db) => _db = db;

    private sealed record Row(
        int Locations, int CatalogItems, int StaffMembers, bool HasGateway, string? PrimaryDomain,
        Guid? OwnFranchiseId);

    public async Task<OnboardingFacts?> GetAsync(Guid brandId, CancellationToken ct = default)
    {
        var rows = await _db.Database.SqlQuery<Row>($"""
            SELECT locations       AS "Locations",
                   catalog_items   AS "CatalogItems",
                   staff_members   AS "StaffMembers",
                   coalesce(has_gateway, false) AS "HasGateway",
                   primary_domain  AS "PrimaryDomain",
                   own_franchise_id AS "OwnFranchiseId"
            FROM kernel.brand_onboarding_facts({brandId})
            """).ToListAsync(ct);

        if (rows.Count == 0) return null;
        var r = rows[0];
        return new OnboardingFacts(r.Locations, r.CatalogItems, r.StaffMembers, r.HasGateway,
                                   r.PrimaryDomain, r.OwnFranchiseId);
    }

    public async Task<string?> EnsureSubdomainAsync(
        Guid brandId, string baseDomain, CancellationToken ct = default)
    {
        var rows = await _db.Database.SqlQuery<string?>(
            $"SELECT kernel.ensure_brand_subdomain({brandId}, {baseDomain}) AS \"Value\"")
            .ToListAsync(ct);

        return rows.Count > 0 ? rows[0] : null;
    }

    private sealed record IdentityRow(
        string Code, string Name, string? PrimaryColor, string? LogoUrl,
        string? SupportEmail, string? SupportPhone, string? PrimaryDomain);

    public async Task<BrandAppIdentity?> GetAppIdentityAsync(Guid brandId, CancellationToken ct = default)
    {
        var rows = await _db.Database.SqlQuery<IdentityRow>($"""
            SELECT code           AS "Code",
                   name           AS "Name",
                   primary_color  AS "PrimaryColor",
                   logo_url       AS "LogoUrl",
                   support_email  AS "SupportEmail",
                   support_phone  AS "SupportPhone",
                   primary_domain AS "PrimaryDomain"
            FROM kernel.brand_app_identity({brandId})
            """).ToListAsync(ct);

        if (rows.Count == 0) return null;
        var r = rows[0];
        return new BrandAppIdentity(r.Code, r.Name, r.PrimaryColor, r.LogoUrl,
                                    r.SupportEmail, r.SupportPhone, r.PrimaryDomain);
    }
}
