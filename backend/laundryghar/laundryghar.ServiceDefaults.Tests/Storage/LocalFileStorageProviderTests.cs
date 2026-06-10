using laundryghar.ServiceDefaults.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace laundryghar.ServiceDefaults.Tests.Storage;

public sealed class LocalFileStorageProviderTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileStorageProvider _sut;

    public LocalFileStorageProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lgtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _sut = new LocalFileStorageProvider(
            Options.Create(new LocalStorageOptions { RootPath = _root }),
            NullLogger<LocalFileStorageProvider>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── SaveAsync / OpenReadAsync round-trip ──────────────────────────────────

    [Fact]
    public async Task SaveThenRead_RoundTrips()
    {
        var brandId = Guid.NewGuid();
        var payload = "hello local storage"u8.ToArray();

        using var input = new MemoryStream(payload);
        var key = await _sut.SaveAsync(input, "image/jpeg", "inspections", brandId);

        await using var stream = await _sut.OpenReadAsync(key);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public async Task SaveAsync_CreatesSubdirectories()
    {
        var brandId = Guid.NewGuid();
        using var content = new MemoryStream("x"u8.ToArray());
        var key = await _sut.SaveAsync(content, "image/png", "proof", brandId);

        // Verify the file actually exists on disk
        var fullPath = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath));
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        var brandId = Guid.NewGuid();
        using var content = new MemoryStream("data"u8.ToArray());
        var key = await _sut.SaveAsync(content, "image/jpeg", "inspections", brandId);

        await _sut.DeleteAsync(key);

        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.OpenReadAsync(key));
    }

    [Fact]
    public async Task DeleteAsync_NonExistentKey_DoesNotThrow()
    {
        // Should silently no-op
        var exception = await Record.ExceptionAsync(() =>
            _sut.DeleteAsync("00000000000000000000000000000000/test/nonexistent.jpg"));
        Assert.Null(exception);
    }

    // ── OpenReadAsync missing file ────────────────────────────────────────────

    [Fact]
    public async Task OpenReadAsync_MissingKey_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.OpenReadAsync("00000000000000000000000000000000/missing/file.jpg"));
    }

    // ── GetPublicUrl ──────────────────────────────────────────────────────────

    [Fact]
    public void GetPublicUrl_AlwaysReturnsNull()
    {
        Assert.Null(_sut.GetPublicUrl("any/key.jpg"));
    }

    // ── Path traversal defence ────────────────────────────────────────────────

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    public async Task OpenReadAsync_PathTraversalKey_ThrowsInvalidOperation(string maliciousKey)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.OpenReadAsync(maliciousKey));
    }

    // ── Constructor validation ────────────────────────────────────────────────

    [Fact]
    public void Constructor_EmptyRootPath_ThrowsInvalidOperation()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new LocalFileStorageProvider(
                Options.Create(new LocalStorageOptions { RootPath = "" }),
                NullLogger<LocalFileStorageProvider>.Instance));
    }
}
