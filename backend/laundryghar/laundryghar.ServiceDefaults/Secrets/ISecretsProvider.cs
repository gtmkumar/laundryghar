namespace laundryghar.ServiceDefaults.Secrets;

/// <summary>
/// Supplies a flat set of config key/value pairs that are layered into
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> at startup.
///
/// Keys follow the standard .NET config convention: colon-separated segments
/// (e.g. <c>ConnectionStrings:Default</c>, <c>Jwt:PrivateKey</c>). When sourced
/// from the file-system the double-underscore (<c>__</c>) separator is also
/// accepted and is normalised to <c>:</c> automatically.
///
/// Implementations MUST be stateless after construction; <see cref="LoadAsync"/>
/// may be called more than once during bootstrap.
/// </summary>
public interface ISecretsProvider
{
    /// <summary>
    /// Returns zero or more config entries to merge into <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
    /// An empty sequence is a valid (no-op) result.
    /// </summary>
    Task<IEnumerable<KeyValuePair<string, string>>> LoadAsync(CancellationToken cancellationToken = default);
}
