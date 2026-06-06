using laundryghar.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace laundryghar.ServiceDefaults.Tests.Secrets;

/// <summary>
/// Unit tests for <see cref="FileSecretsProvider"/>.
/// These are pure filesystem tests — no ASP.NET host or database required.
/// </summary>
public sealed class FileSecretsProviderTests : IDisposable
{
    private readonly string _tempDir;

    public FileSecretsProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lg-secrets-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── Core behaviour ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_DoubleUnderscore_NormalisedToColon()
    {
        // Arrange — Docker/k8s secret-mount convention: filename uses __ as separator
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "ConnectionStrings__Default"),
            "Host=test-db;Database=test");

        var sut = new FileSecretsProvider(_tempDir);

        // Act
        var entries = (await sut.LoadAsync()).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Assert — __ → : normalisation
        Assert.True(entries.ContainsKey("ConnectionStrings:Default"),
            "Expected key 'ConnectionStrings:Default' after __ → : normalisation.");
        Assert.Equal("Host=test-db;Database=test", entries["ConnectionStrings:Default"]);
    }

    [Fact]
    public async Task LoadAsync_LeadingAndTrailingWhitespace_Trimmed()
    {
        // Mounted secret files often contain a trailing newline
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "Jwt__PrivateKey"),
            "  -----BEGIN RSA PRIVATE KEY-----\nABC123\n-----END RSA PRIVATE KEY-----\n  ");

        var sut     = new FileSecretsProvider(_tempDir);
        var entries = (await sut.LoadAsync()).ToDictionary(kv => kv.Key, kv => kv.Value);

        var value = entries["Jwt:PrivateKey"];
        Assert.False(value.StartsWith(' '),  "Value should not start with whitespace.");
        Assert.False(value.EndsWith('\n'), "Value should not end with newline.");
    }

    [Fact]
    public async Task LoadAsync_MultipleFiles_AllLoaded()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ConnectionStrings__Default"), "val-default");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ConnectionStrings__Admin"),   "val-admin");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Jwt__PrivateKey"),            "val-pk");

        var sut     = new FileSecretsProvider(_tempDir);
        var entries = (await sut.LoadAsync()).ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(3, entries.Count);
        Assert.Equal("val-default", entries["ConnectionStrings:Default"]);
        Assert.Equal("val-admin",   entries["ConnectionStrings:Admin"]);
        Assert.Equal("val-pk",      entries["Jwt:PrivateKey"]);
    }

    [Fact]
    public async Task LoadAsync_EmptyDirectory_ReturnsEmpty()
    {
        var sut     = new FileSecretsProvider(_tempDir);
        var entries = await sut.LoadAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task LoadAsync_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");
        var sut = new FileSecretsProvider(nonExistent);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => sut.LoadAsync());
    }

    // ── IConfigurationBuilder integration ────────────────────────────────────────

    [Fact]
    public async Task SecretsConfigurationSource_SurfacesInIConfiguration()
    {
        // Arrange — write a secret file
        const string expectedValue = "Host=prod-db;Database=laundry_ghar_db;Username=app_user;Password=supersecret";
        await File.WriteAllTextAsync(
            Path.Combine(_tempDir, "ConnectionStrings__Default"),
            expectedValue);

        var provider = new FileSecretsProvider(_tempDir);
        var source   = new SecretsConfigurationSource(provider);

        // Act — build a configuration that contains only this source
        var configuration = new ConfigurationBuilder()
            .Add(source)
            .Build();

        // Assert — accessible via standard IConfiguration indexer and GetConnectionString
        Assert.Equal(expectedValue, configuration["ConnectionStrings:Default"]);
        Assert.Equal(expectedValue, configuration.GetConnectionString("Default"));
    }

    // ── EnvironmentSecretsProvider ────────────────────────────────────────────────

    [Fact]
    public async Task EnvironmentSecretsProvider_ReturnsEmpty_Always()
    {
        var sut     = new EnvironmentSecretsProvider();
        var entries = await sut.LoadAsync();

        Assert.Empty(entries);
    }
}
