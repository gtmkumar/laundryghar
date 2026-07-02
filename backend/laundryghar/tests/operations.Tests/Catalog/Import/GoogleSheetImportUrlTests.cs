using laundryghar.Utilities.Exceptions;
using operations.Application.Catalog.Catalog.Commands.Item;
using Xunit;

namespace operations.Tests.Catalog.Import;

/// <summary>
/// URL accept/reject matrix + gid resolution for the Google Sheet import (#21). Exercises the pure
/// <see cref="ParseGoogleSheetImportHandler.ParseAndValidateUrl"/> SSRF guard directly — no HTTP.
/// </summary>
public class GoogleSheetImportUrlTests
{
    private const string Id = "1AbC-dEf_gHiJKlmnOpQrStUvWxYz0123456789";

    [Fact]
    public void Accepts_canonical_share_url_and_defaults_gid_to_zero()
    {
        var (sheetId, gid) = ParseGoogleSheetImportHandler.ParseAndValidateUrl(
            $"https://docs.google.com/spreadsheets/d/{Id}/edit", null);
        Assert.Equal(Id, sheetId);
        Assert.Equal("0", gid);
    }

    [Fact]
    public void Extracts_gid_from_url_fragment()
    {
        var (_, gid) = ParseGoogleSheetImportHandler.ParseAndValidateUrl(
            $"https://docs.google.com/spreadsheets/d/{Id}/edit#gid=87654321", null);
        Assert.Equal("87654321", gid);
    }

    [Fact]
    public void Extracts_gid_from_query_string()
    {
        var (_, gid) = ParseGoogleSheetImportHandler.ParseAndValidateUrl(
            $"https://docs.google.com/spreadsheets/d/{Id}/export?format=csv&gid=42", null);
        Assert.Equal("42", gid);
    }

    [Fact]
    public void Explicit_gid_param_wins_over_url_gid()
    {
        var (_, gid) = ParseGoogleSheetImportHandler.ParseAndValidateUrl(
            $"https://docs.google.com/spreadsheets/d/{Id}/edit#gid=111", "999");
        Assert.Equal("999", gid);
    }

    [Theory]
    [InlineData("http://docs.google.com/spreadsheets/d/xyz/edit")]          // http scheme
    [InlineData("https://evil.com/spreadsheets/d/xyz/edit")]                // wrong host
    [InlineData("https://docs.google.com.evil.com/spreadsheets/d/xyz")]     // suffix-spoof host
    [InlineData("https://docs-google.com/spreadsheets/d/xyz/edit")]         // look-alike host
    [InlineData("https://accounts.google.com/ServiceLogin")]               // auth host
    [InlineData("https://docs.google.com/document/d/xyz/edit")]            // a Doc, not a Sheet
    [InlineData("https://docs.google.com/../../etc/passwd")]                // path traversal
    [InlineData("not-a-url")]
    [InlineData("")]
    public void Rejects_untrusted_or_malformed_urls(string url)
    {
        Assert.Throws<ValidationException>(() =>
            ParseGoogleSheetImportHandler.ParseAndValidateUrl(url, null));
    }

    [Fact]
    public void Rejects_non_numeric_explicit_gid()
    {
        Assert.Throws<ValidationException>(() =>
            ParseGoogleSheetImportHandler.ParseAndValidateUrl(
                $"https://docs.google.com/spreadsheets/d/{Id}/edit", "sheet1"));
    }
}
