using laundryghar.ServiceDefaults.Storage;

namespace laundryghar.ServiceDefaults.Tests.Storage;

public sealed class FileStorageKeyGeneratorTests
{
    // ── Generate ───────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_ReturnsBrandPrefixedKey()
    {
        var brandId = Guid.NewGuid();
        var key = FileStorageKeyGenerator.Generate(brandId, "inspections", "image/jpeg");

        Assert.StartsWith(brandId.ToString("N") + "/inspections/", key);
    }

    [Fact]
    public void Generate_EndsWithCorrectExtension_Jpeg()
    {
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), "inspections", "image/jpeg");
        Assert.EndsWith(".jpg", key);
    }

    [Fact]
    public void Generate_EndsWithCorrectExtension_Png()
    {
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), "inspections", "image/png");
        Assert.EndsWith(".png", key);
    }

    [Fact]
    public void Generate_EndsWithCorrectExtension_Webp()
    {
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), "proof", "image/webp");
        Assert.EndsWith(".webp", key);
    }

    [Fact]
    public void Generate_UnknownMimeType_FallsBackToBin()
    {
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), "docs", "application/octet-stream");
        Assert.EndsWith(".bin", key);
    }

    [Fact]
    public void Generate_MimeTypeWithParameters_StillResolvesCorrectly()
    {
        // "image/jpeg; charset=utf-8" should resolve to jpg
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), "inspections", "image/jpeg; charset=utf-8");
        Assert.EndsWith(".jpg", key);
    }

    [Fact]
    public void Generate_TwoCallsProduceDifferentKeys()
    {
        var brandId = Guid.NewGuid();
        var k1 = FileStorageKeyGenerator.Generate(brandId, "inspections", "image/jpeg");
        var k2 = FileStorageKeyGenerator.Generate(brandId, "inspections", "image/jpeg");
        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void Generate_KeyContainsNoDoubleDots()
    {
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), "inspections", "image/jpeg");
        Assert.DoesNotContain("..", key);
    }

    [Fact]
    public void Generate_KeyContainsNoBackslashes()
    {
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), "inspections", "image/jpeg");
        Assert.DoesNotContain("\\", key);
    }

    [Fact]
    public void Generate_KeyHasThreeSegments()
    {
        // Expected shape: {brand}/{area}/{uuid}.{ext} — exactly 3 slash-delimited segments
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), "inspections", "image/jpeg");
        Assert.Equal(3, key.Split('/').Length);
    }

    // ── Area validation ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("foo bar")]
    [InlineData("FOO")]
    [InlineData("inspections!")]
    [InlineData("")]
    public void Generate_UnsafeArea_ThrowsArgumentException(string area)
    {
        Assert.Throws<ArgumentException>(() =>
            FileStorageKeyGenerator.Generate(Guid.NewGuid(), area, "image/jpeg"));
    }

    [Theory]
    [InlineData("inspections")]
    [InlineData("proof")]
    [InlineData("signature")]
    [InlineData("docs/kyc")]
    [InlineData("bc5/proof")]
    public void Generate_SafeArea_DoesNotThrow(string area)
    {
        // Should not throw
        var key = FileStorageKeyGenerator.Generate(Guid.NewGuid(), area, "image/jpeg");
        Assert.NotNull(key);
    }

    // ── ResolveExtension ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/jpg",  "jpg")]
    [InlineData("image/png",  "png")]
    [InlineData("image/webp", "webp")]
    [InlineData("image/gif",  "gif")]
    [InlineData("application/pdf", "pdf")]
    [InlineData("application/octet-stream", "bin")]
    [InlineData("text/plain", "bin")]
    public void ResolveExtension_ReturnsExpected(string mime, string expected)
    {
        Assert.Equal(expected, FileStorageKeyGenerator.ResolveExtension(mime));
    }
}
