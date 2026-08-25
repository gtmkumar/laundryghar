using core.Application.Common.Interfaces;
using core.Application.Identity.Signup.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace core.Application.Identity.Signup.Queries;

public sealed record GetSignupTemplatesQuery : IQuery<IReadOnlyList<SignupTemplateDto>>;

/// <summary>
/// The templates a prospective provider may launch on (§9 "pick vertical template + plan").
/// Anonymous — this is the shop window, shown before anyone has an account.
///
/// <para>Only <c>is_public</c> templates are offered. A template whose fulfilment mode has no
/// strategy would let someone sign up for a business that cannot take an order, so it stays hidden
/// until it can actually run.</para>
/// </summary>
public class GetSignupTemplatesQueryHandler
    : IQueryHandler<GetSignupTemplatesQuery, IReadOnlyList<SignupTemplateDto>>
{
    private readonly ICoreDbContext _db;
    public GetSignupTemplatesQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<IReadOnlyList<SignupTemplateDto>> HandleAsync(
        GetSignupTemplatesQuery q, CancellationToken ct)
    {
        var rows = await _db.VerticalTemplates.AsNoTracking()
            .Where(t => t.IsPublic)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Key)
            .ToListAsync(ct);

        return rows.Select(t => new SignupTemplateDto(
            t.Key, t.VerticalKey, t.Name, t.Description, t.FulfillmentMode, t.DefaultBundleCode,
            CategoryNames(t.CatalogSeed))).ToList();
    }

    /// <summary>Category names only — enough for the signup screen to say "you'll start with Wash &amp;
    /// Fold, Dry Clean, Ironing" without shipping the whole seed to an anonymous caller.</summary>
    private static IReadOnlyList<string> CategoryNames(string catalogSeed)
    {
        try
        {
            using var doc = JsonDocument.Parse(catalogSeed);
            return doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("category", out var c) ? c.GetString() : null)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList();
        }
        catch (JsonException)
        {
            // A malformed seed must not take the signup page down — it just shows no preview.
            return [];
        }
    }
}
