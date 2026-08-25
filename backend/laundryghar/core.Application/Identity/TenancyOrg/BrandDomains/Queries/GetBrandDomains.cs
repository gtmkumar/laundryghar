using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.BrandDomains.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.TenancyOrg.BrandDomains.Queries;

public sealed record GetBrandDomainsQuery(Guid BrandId) : IQuery<IReadOnlyList<BrandDomainDto>>;

/// <summary>Every custom domain registered for a brand, with the DNS instruction each one needs.</summary>
public class GetBrandDomainsQueryHandler
    : IQueryHandler<GetBrandDomainsQuery, IReadOnlyList<BrandDomainDto>>
{
    private readonly ICoreDbContext _db;
    private readonly BrandDomainSettings _settings;

    public GetBrandDomainsQueryHandler(ICoreDbContext db, IOptions<BrandDomainSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<BrandDomainDto>> HandleAsync(GetBrandDomainsQuery q, CancellationToken ct)
    {
        var rows = await _db.BrandDomains.AsNoTracking()
            .Where(d => d.BrandId == q.BrandId)
            // Primary first, then the verified ones, then newest — the order an operator wants.
            .OrderByDescending(d => d.IsPrimary)
            .ThenByDescending(d => d.VerifiedAt != null)
            .ThenByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(d => new BrandDomainDto(
            d.Id, d.BrandId, d.Domain,
            Verified: d.VerifiedAt is not null,
            d.VerifiedAt, d.SslStatus, d.IsPrimary,
            VerificationName: BrandDomainChallenge.NameFor(d.Domain),
            VerificationValue: d.VerificationTxt,
            CnameTarget: _settings.CnameTarget,
            d.CreatedAt)).ToList();
    }
}
