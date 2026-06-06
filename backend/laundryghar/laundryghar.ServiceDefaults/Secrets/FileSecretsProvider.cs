namespace laundryghar.ServiceDefaults.Secrets;

/// <summary>
/// Reads secrets from a directory following the Docker / Kubernetes secret-mount
/// convention: each file's <em>name</em> is the config key and its <em>content</em>
/// is the value.
///
/// Key normalisation: double-underscores (<c>__</c>) in file names are converted
/// to colons (<c>:</c>) so that a file named <c>ConnectionStrings__Default</c>
/// surfaces as <c>ConnectionStrings:Default</c> in <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
///
/// Configure the directory via <c>Secrets:FilePath</c> in any config source
/// (appsettings.json, environment variable <c>Secrets__FilePath</c>, etc.).
/// The provider skips files that cannot be read (e.g. permission denied) and logs
/// a warning rather than failing startup — callers that need a specific secret
/// will throw their own <see cref="InvalidOperationException"/> at first access.
///
/// Activated when <c>Secrets:Provider</c> is set to <c>file</c>.
/// </summary>
internal sealed class FileSecretsProvider : ISecretsProvider
{
    // Upper bound on a single secret file. Real secrets (connection strings, PEM keys)
    // are well under this; the guard stops an accidentally-mounted large file (a log,
    // a blob) from being slurped into memory and OOM-killing the service at startup.
    private const long MaxSecretFileSizeBytes = 64 * 1024; // 64 KiB

    private readonly string _directory;

    /// <param name="directory">
    /// Absolute path to the directory that contains the secret files.
    /// Must not be null or empty; the directory must exist at the time
    /// <see cref="LoadAsync"/> is called.
    /// </param>
    public FileSecretsProvider(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <inheritdoc />
    public Task<IEnumerable<KeyValuePair<string, string>>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
            throw new DirectoryNotFoundException(
                $"Secrets:FilePath directory not found: '{_directory}'. " +
                "Ensure the secret-mount directory exists before startup.");

        var entries = new List<KeyValuePair<string, string>>();

        foreach (var filePath in Directory.EnumerateFiles(_directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // File name → config key: replace __ with : (Docker/k8s mount convention)
            var fileName  = Path.GetFileName(filePath);
            var configKey = fileName.Replace("__", ":", StringComparison.Ordinal);

            try
            {
                var length = new FileInfo(filePath).Length;
                if (length > MaxSecretFileSizeBytes)
                {
                    Console.Error.WriteLine(
                        $"[FileSecretsProvider] Warning: secret file '{fileName}' is {length} bytes " +
                        $"(> {MaxSecretFileSizeBytes} limit) and was skipped.");
                    continue;
                }

                var value = File.ReadAllText(filePath).Trim();
                entries.Add(new KeyValuePair<string, string>(configKey, value));
            }
            catch (IOException ex)
            {
                // Warn rather than hard-fail — startup exception for the missing value
                // is the service's own responsibility (e.g. GetConnectionString ?? throw).
                Console.Error.WriteLine(
                    $"[FileSecretsProvider] Warning: could not read secret file '{filePath}': {ex.Message}");
            }
        }

        return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(entries);
    }
}
