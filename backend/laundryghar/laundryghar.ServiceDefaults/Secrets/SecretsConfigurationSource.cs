using Microsoft.Extensions.Configuration;

namespace laundryghar.ServiceDefaults.Secrets;

/// <summary>
/// Bridges <see cref="ISecretsProvider"/> into the .NET configuration pipeline as a
/// standard <see cref="IConfigurationSource"/>. Registered at the END of the
/// <see cref="IConfigurationBuilder"/> sources list so it sits above
/// <c>appsettings.json</c> / <c>appsettings.{env}.json</c> in the last-wins
/// evaluation order, but below environment variables which .NET's host builder
/// adds after this point.
///
/// In practice:
/// <list type="bullet">
///   <item><c>env</c> provider — contributes nothing; existing config is unchanged.</item>
///   <item><c>file</c> provider — injects mounted secrets that were absent from appsettings.</item>
/// </list>
/// </summary>
internal sealed class SecretsConfigurationSource : IConfigurationSource
{
    private readonly ISecretsProvider _provider;

    public SecretsConfigurationSource(ISecretsProvider provider)
        => _provider = provider;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new SecretsConfigurationProvider(_provider);
}

/// <summary>
/// Loads secrets from <see cref="ISecretsProvider"/> synchronously at config-build
/// time. The async load is block-waited here because <see cref="IConfigurationProvider.Load"/>
/// is a synchronous contract. Providers that perform real I/O (disk, network) should
/// complete quickly enough that this is not a concern in practice.
/// </summary>
internal sealed class SecretsConfigurationProvider : ConfigurationProvider
{
    private readonly ISecretsProvider _secretsProvider;

    public SecretsConfigurationProvider(ISecretsProvider secretsProvider)
        => _secretsProvider = secretsProvider;

    public override void Load()
    {
        // GetAwaiter().GetResult() is intentional: IConfigurationProvider.Load() is
        // synchronous. File I/O is fast; network-backed providers (Key Vault, SSM)
        // should remain bounded by their own timeouts.
        var entries = _secretsProvider.LoadAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Data = entries.ToDictionary(
            kv => kv.Key,
            kv => (string?)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }
}
