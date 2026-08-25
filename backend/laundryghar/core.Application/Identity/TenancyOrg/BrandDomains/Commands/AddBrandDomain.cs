using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.BrandDomains.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace core.Application.Identity.TenancyOrg.BrandDomains.Commands;

public sealed record AddBrandDomainCommand(Guid BrandId, AddBrandDomainRequest Request, Guid? ActorId)
    : ICommand<BrandDomainDto>;

/// <summary>
/// Registers a custom domain for a brand and issues its ownership challenge.
///
/// <para>The row is created UNVERIFIED. It has no effect on traffic until
/// <c>VerifyBrandDomainCommand</c> stamps <c>verified_at</c> — that is the whole point of the
/// two-step flow, and why merely adding a domain here is safe.</para>
/// </summary>
public class AddBrandDomainCommandHandler : ICommandHandler<AddBrandDomainCommand, BrandDomainDto>
{
    private readonly ICoreDbContext _db;
    private readonly BrandDomainSettings _settings;

    public AddBrandDomainCommandHandler(ICoreDbContext db, IOptions<BrandDomainSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
    }

    public async Task<BrandDomainDto> HandleAsync(AddBrandDomainCommand cmd, CancellationToken ct)
    {
        var domain = BrandDomainChallenge.Normalize(cmd.Request.Domain);

        if (!BrandDomainChallenge.IsPlausibleDomain(domain))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["domain"] = ["Enter a valid public domain name, e.g. shop.example.com."],
            });

        // Claim check. The DB's UNIQUE(domain) is the real guarantee (it is what makes the Host
        // lookup total and is race-proof); this is here to return a useful message instead of a
        // constraint violation. Deliberately does NOT say whether the domain belongs to another
        // brand — that would leak the tenant map to anyone who can add a domain.
        //
        // ToLower() is REQUIRED. `domain` is a citext column, but EF sends the comparand as a TEXT
        // parameter, and `citext = text` resolves to the case-SENSITIVE text operator
        // (SELECT 'ABC'::citext = 'abc'::text -> f). A plain `d.Domain == domain` would therefore
        // miss an existing row stored with different casing and let the request fall through to a
        // raw 23505 instead of this message. Lowering both sides sidesteps the operator choice.
        if (await _db.BrandDomains.AnyAsync(d => d.Domain.ToLower() == domain, ct))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["domain"] = ["This domain is already registered."],
            });

        var now = DateTimeOffset.UtcNow;

        // At most one primary per brand (partial unique index). Demote the incumbent rather than
        // letting the insert fail, so "make this the primary" is a single, obvious action.
        if (cmd.Request.IsPrimary)
        {
            var incumbents = await _db.BrandDomains
                .Where(d => d.BrandId == cmd.BrandId && d.IsPrimary)
                .ToListAsync(ct);
            foreach (var i in incumbents)
            {
                i.IsPrimary = false;
                i.UpdatedAt = now;
                i.UpdatedBy = cmd.ActorId;
            }
        }

        var row = new BrandDomain
        {
            Id = Guid.NewGuid(),
            BrandId = cmd.BrandId,
            Domain = domain,
            VerificationTxt = BrandDomainChallenge.NewValue(),
            VerifiedAt = null,
            SslStatus = BrandDomainSslStatus.Pending,
            IsPrimary = cmd.Request.IsPrimary,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = cmd.ActorId,
            UpdatedBy = cmd.ActorId,
        };

        _db.BrandDomains.Add(row);
        await _db.SaveChangesAsync(ct);

        return new BrandDomainDto(
            row.Id, row.BrandId, row.Domain,
            Verified: false, VerifiedAt: null, row.SslStatus, row.IsPrimary,
            VerificationName: BrandDomainChallenge.NameFor(row.Domain),
            VerificationValue: row.VerificationTxt,
            CnameTarget: _settings.CnameTarget,
            row.CreatedAt);
    }
}
