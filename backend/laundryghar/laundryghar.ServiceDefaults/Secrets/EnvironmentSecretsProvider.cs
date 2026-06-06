namespace laundryghar.ServiceDefaults.Secrets;

/// <summary>
/// Pass-through (no-op) implementation of <see cref="ISecretsProvider"/>.
///
/// In Development — and any environment where secrets are already injected as
/// environment variables or via <c>appsettings.*.json</c> — this provider
/// contributes nothing, guaranteeing config resolution is byte-for-byte
/// identical to the baseline .NET configuration pipeline.
///
/// This is the default when <c>Secrets:Provider</c> is absent or set to <c>env</c>.
/// </summary>
internal sealed class EnvironmentSecretsProvider : ISecretsProvider
{
    /// <inheritdoc />
    public Task<IEnumerable<KeyValuePair<string, string>>> LoadAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(Enumerable.Empty<KeyValuePair<string, string>>());
}
