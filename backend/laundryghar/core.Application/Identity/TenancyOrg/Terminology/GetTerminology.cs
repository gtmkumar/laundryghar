using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Terminology;

/// <summary>One vertical's user-facing vocabulary, keyed by term.</summary>
/// <param name="VerticalKey">The vertical these words belong to.</param>
/// <param name="Terms">term key → the word. Clients look up by key and fall back to their own
/// neutral default when a key is absent, so a missing term is a generic word, never a blank.</param>
public sealed record TerminologyPackDto(string VerticalKey, IReadOnlyDictionary<string, TermDto> Terms);

public sealed record TermDto(string Singular, string Plural);

public sealed record GetTerminologyQuery(string? VerticalKey = null) : IQuery<TerminologyPackDto>;

/// <summary>
/// Serves the terminology pack (PLATFORM_STRATEGY.md §3 "terminology is config, not code").
///
/// <para>§12 names terminology leakage as a top risk — "laundry words in a courier UI kills
/// credibility" — and the failure mode is silent: nothing errors, the product just reads wrong. So
/// the words live in a table and every client reads them from here, rather than each of the four
/// front-ends keeping its own copy and drifting.</para>
///
/// <para>With no vertical supplied, resolves the caller's brand's vertical; failing that, laundry —
/// the flagship, and the vocabulary every existing string already uses.</para>
/// </summary>
public class GetTerminologyQueryHandler : IQueryHandler<GetTerminologyQuery, TerminologyPackDto>
{
    /// <summary>Used when neither an explicit vertical nor a brand context is available.</summary>
    public const string DefaultVertical = "laundry";

    private readonly ICoreDbContext _db;
    private readonly laundryghar.Utilities.Services.ICurrentUser _user;

    public GetTerminologyQueryHandler(ICoreDbContext db, laundryghar.Utilities.Services.ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<TerminologyPackDto> HandleAsync(GetTerminologyQuery q, CancellationToken ct)
    {
        var vertical = q.VerticalKey;

        if (string.IsNullOrWhiteSpace(vertical) && _user.TryGetBrandId() is { } brandId)
        {
            vertical = await _db.Brands.AsNoTracking()
                .Where(b => b.Id == brandId)
                .Select(b => b.VerticalKey)
                .FirstOrDefaultAsync(ct);
        }

        vertical = string.IsNullOrWhiteSpace(vertical) ? DefaultVertical : vertical;

        var terms = await _db.VerticalTerms.AsNoTracking()
            .Where(t => t.VerticalKey == vertical)
            .Select(t => new { t.TermKey, t.Singular, t.Plural })
            .ToListAsync(ct);

        return new TerminologyPackDto(
            vertical,
            terms.ToDictionary(
                t => t.TermKey,
                // Plural is optional in the schema; falling back to the singular is better than
                // emitting null and making every client handle it.
                t => new TermDto(t.Singular, t.Plural ?? t.Singular),
                StringComparer.OrdinalIgnoreCase));
    }
}
