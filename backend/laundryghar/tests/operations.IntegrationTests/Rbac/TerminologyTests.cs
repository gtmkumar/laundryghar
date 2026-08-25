using Npgsql;
using Xunit;

namespace operations.IntegrationTests.Rbac;

/// <summary>
/// The per-vertical vocabulary (PLATFORM_STRATEGY.md §3 "terminology is config, not code").
///
/// <para>§12 names terminology leakage as a top risk — "laundry words in a courier UI kills
/// credibility" — and it fails SILENTLY: nothing errors, the product just reads wrong to the one
/// audience that would notice. So the completeness check is the important test here, not the lookup.</para>
/// </summary>
[Collection("rbac-ef")]
public sealed class TerminologyTests
{
    private readonly RbacEfFixture _fx;
    public TerminologyTests(RbacEfFixture fx) => _fx = fx;

    // 1 ── EVERY shipped vertical defines EVERY term. This is the one that stops a laundry word
    //      reaching a courier screen: add a vertical without its vocabulary and this goes red.
    [Fact]
    public async Task every_vertical_defines_every_term()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT string_agg(v.vertical_key || '.' || t.term_key, ', ')
            FROM   (SELECT unnest(ARRAY['laundry','salon','logistics','tiffin']) AS vertical_key) v
            CROSS JOIN (SELECT DISTINCT term_key FROM identity_access.vertical_terms) t
            WHERE  NOT EXISTS (SELECT 1 FROM identity_access.vertical_terms x
                                WHERE x.vertical_key = v.vertical_key AND x.term_key = t.term_key)
            """, conn);

        var gaps = await cmd.ExecuteScalarAsync();
        Assert.True(gaps is null or DBNull, $"verticals missing terms: {gaps}");
    }

    // 2 ── the words are actually DIFFERENT per vertical. A pack that dutifully returns "garment"
    //      for every vertical would satisfy test 1 and defeat the entire purpose.
    [Fact]
    public async Task the_item_noun_differs_across_verticals()
    {
        if (!_fx.DockerAvailable) return;

        var byVertical = await ReadTermAsync("item");

        Assert.Equal("garment", byVertical["laundry"]);
        Assert.Equal("service",  byVertical["salon"]);
        Assert.Equal("parcel",   byVertical["logistics"]);
        Assert.Equal("meal",     byVertical["tiffin"]);

        // …and all four are distinct, which is the property that actually matters.
        Assert.Equal(4, byVertical.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // 3 ── the on-site location noun, which is the term the admin console already varies by hand in
    //      verticalTerms.ts. Backend and client must agree, or they drift.
    [Fact]
    public async Task the_onsite_location_noun_matches_the_admin_console()
    {
        if (!_fx.DockerAvailable) return;

        var byVertical = await ReadTermAsync("onsite_location");

        Assert.Equal("Warehouse", byVertical["laundry"]);
        Assert.Equal("Studio",    byVertical["salon"]);
        Assert.Equal("Hub",       byVertical["logistics"]);
        Assert.Equal("Kitchen",   byVertical["tiffin"]);
    }

    // 4 ── every term has a plural. Clients render counts ("3 garments"), and a missing plural shows
    //      up as "3 garment" — small, but exactly the kind of wrongness §12 is about.
    [Fact]
    public async Task every_term_has_a_plural()
    {
        if (!_fx.DockerAvailable) return;

        await using var conn = await _fx.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT string_agg(vertical_key || '.' || term_key, ', ') " +
            "FROM identity_access.vertical_terms WHERE plural IS NULL OR btrim(plural) = ''", conn);

        var missing = await cmd.ExecuteScalarAsync();
        Assert.True(missing is null or DBNull, $"terms with no plural: {missing}");
    }

    private async Task<Dictionary<string, string>> ReadTermAsync(string termKey)
    {
        await using var conn = await _fx.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT vertical_key, singular FROM identity_access.vertical_terms WHERE term_key = @k", conn);
        cmd.Parameters.AddWithValue("@k", termKey);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) map[reader.GetString(0)] = reader.GetString(1);
        return map;
    }
}
