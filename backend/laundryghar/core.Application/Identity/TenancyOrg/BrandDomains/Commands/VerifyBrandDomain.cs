using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.BrandDomains.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.BrandDomains.Commands;

public sealed record VerifyBrandDomainCommand(Guid BrandId, Guid DomainId, Guid? ActorId)
    : ICommand<VerifyBrandDomainResultDto?>;

/// <summary>
/// Checks the provider's DNS for the challenge we issued and, if present, stamps
/// <c>verified_at</c> — the moment the domain starts resolving real traffic
/// (<c>kernel.resolve_brand_domain</c> returns only verified rows).
///
/// <para>Because stamping <c>verified_at</c> is what makes a hostname live, this handler is
/// deliberately conservative: it verifies ONLY on a positive match of the exact value we generated,
/// and it never un-verifies. A DNS outage must not take a provider's site down, and a provider who
/// tidies up their zone months later should not silently lose their domain.</para>
/// </summary>
public class VerifyBrandDomainCommandHandler
    : ICommandHandler<VerifyBrandDomainCommand, VerifyBrandDomainResultDto?>
{
    private readonly ICoreDbContext _db;
    private readonly IDnsTxtLookup _dns;

    public VerifyBrandDomainCommandHandler(ICoreDbContext db, IDnsTxtLookup dns)
    {
        _db = db;
        _dns = dns;
    }

    public async Task<VerifyBrandDomainResultDto?> HandleAsync(VerifyBrandDomainCommand cmd, CancellationToken ct)
    {
        // Scoped by BrandId as well as Id: a caller must not be able to verify (or probe for)
        // another brand's domain row by guessing its id.
        var row = await _db.BrandDomains
            .FirstOrDefaultAsync(d => d.Id == cmd.DomainId && d.BrandId == cmd.BrandId, ct);

        if (row is null) return null;

        if (row.VerifiedAt is not null)
            return new VerifyBrandDomainResultDto(
                true, BrandDomainVerifyStatus.AlreadyVerified,
                "This domain is already verified.", []);

        var name = BrandDomainChallenge.NameFor(row.Domain);

        IReadOnlyList<string> records;
        try
        {
            records = await _dns.GetTxtRecordsAsync(name, ct);
        }
        catch (DnsLookupException ex)
        {
            // Nothing was learned about their DNS, so this is NOT a verification failure. Reported
            // as its own status so the console can say "try again" instead of "you got it wrong".
            return new VerifyBrandDomainResultDto(
                false, BrandDomainVerifyStatus.LookupFailed,
                $"Could not query DNS for {name}: {ex.Message} No change was made — try again shortly.",
                []);
        }

        if (records.Count == 0)
            return new VerifyBrandDomainResultDto(
                false, BrandDomainVerifyStatus.RecordNotFound,
                $"No TXT record found at {name}. DNS changes can take a while to propagate.",
                []);

        // Ordinal comparison: the value is our own hex token, and a case- or culture-insensitive
        // match would weaken the only secret in the flow.
        var matched = records.Any(r => string.Equals(r.Trim(), row.VerificationTxt, StringComparison.Ordinal));

        if (!matched)
            return new VerifyBrandDomainResultDto(
                false, BrandDomainVerifyStatus.ValueMismatch,
                $"A TXT record exists at {name} but none matches the expected value.",
                records);

        var now = DateTimeOffset.UtcNow;
        row.VerifiedAt = now;
        row.UpdatedAt = now;
        row.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);

        return new VerifyBrandDomainResultDto(
            true, BrandDomainVerifyStatus.Verified,
            $"{row.Domain} is verified and will now resolve to this brand.", records);
    }
}
