using core.Application.Identity.WhiteLabel.Queries;
using Xunit;

namespace core.Tests.WhiteLabel;

/// <summary>
/// §4 tier T3 — the identifiers a branded build is submitted under (docs/TASKS.md T-22).
///
/// <para>These matter more than their size suggests: a bundle identifier and a package name
/// <b>cannot be changed after the first store submission</b>. Getting one wrong is not a bug you fix
/// in the next release — it is a new app listing, with the old one's installs stranded.</para>
///
/// <para>The bug these were written for: <c>com.laundryghar.lg-main.customer</c> is a perfectly
/// reasonable-looking string and an <b>invalid Android package name</b>, because a package segment
/// must be a valid Java identifier. It read fine in the API response and would have failed at build
/// time, long after anyone was looking.</para>
/// </summary>
public class AppIdentifierTests
{
    // 1 ── THE BUG. Hyphens are legal in an iOS bundle id and illegal in an Android package, and one
    //      string has to satisfy both.
    [Theory]
    [InlineData("LG-MAIN", "lgmain")]
    [InlineData("wash & fold", "washfold")]
    [InlineData("Sparkle_Clean", "sparkleclean")]
    [InlineData("ACME.Laundry", "acmelaundry")]
    public void An_identifier_segment_keeps_only_letters_and_digits(string code, string expected)
    {
        Assert.Equal(expected, GetAppConfigQueryHandler.IdentifierSegment(code));
    }

    // 2 ── a package segment cannot START with a digit, and cannot be empty. Both are real brand
    //      codes somebody will eventually pick.
    [Theory]
    [InlineData("123laundry", "b123laundry")]
    [InlineData("7", "b7")]
    [InlineData("", "b")]
    [InlineData("---", "b")]
    public void An_identifier_segment_is_always_a_valid_java_identifier(string code, string expected)
    {
        var segment = GetAppConfigQueryHandler.IdentifierSegment(code);

        Assert.Equal(expected, segment);
        Assert.True(segment.Length > 0);
        Assert.False(char.IsAsciiDigit(segment[0]));
        Assert.All(segment, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    // 3 ── the SLUG keeps hyphens, because the Expo slug and the URL scheme both accept them and a
    //      multi-word brand is unreadable without.
    [Theory]
    [InlineData("LG-MAIN", "lg-main")]
    [InlineData("Wash & Fold", "wash-fold")]
    [InlineData("  spaced  out  ", "spaced-out")]
    public void A_slug_keeps_hyphens_but_collapses_runs(string code, string expected)
    {
        Assert.Equal(expected, GetAppConfigQueryHandler.Slug(code));
    }

    // 4 ── the run-collapsing is a LOOP, not one pass. "Wash & Fold" maps to "wash---fold", and a
    //      single Replace("--","-") leaves "wash--fold" — the same defect already caught once in the
    //      signup catalogue slug, which is why it is asserted here rather than assumed.
    [Fact]
    public void Long_runs_of_separators_collapse_completely()
    {
        Assert.Equal("a-b", GetAppConfigQueryHandler.Slug("a &&&&& b"));
        Assert.DoesNotContain("--", GetAppConfigQueryHandler.Slug("x  @  #  y"));
    }

    // 5 ── a slug never starts with a digit either: Expo slugs and URL schemes both dislike it.
    [Fact]
    public void A_slug_is_never_digit_initial()
    {
        // The hyphen SURVIVES here — a slug keeps them (test 3); only the leading digit is fixed.
        Assert.Equal("b2024-laundry", GetAppConfigQueryHandler.Slug("2024-laundry"));
    }

    // 6 ── only the apps we actually ship. A typo must produce a 404, not a config for an app that
    //      does not exist — which someone would then try to build.
    [Fact]
    public void Only_the_known_apps_are_offered()
    {
        Assert.Equal(["customer", "rider"], GetAppConfigQueryHandler.KnownApps);
    }
}
