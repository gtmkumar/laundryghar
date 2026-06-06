using Microsoft.Extensions.Configuration;

namespace laundryghar.ServiceDefaults.Secrets;

/// <summary>
/// Reads <c>Secrets:Provider</c> from a partially-built <see cref="IConfiguration"/>
/// (appsettings + env vars already applied) and returns the matching
/// <see cref="ISecretsProvider"/> implementation.
///
/// <para><b>Supported values for <c>Secrets:Provider</c></b></para>
/// <list type="table">
///   <listheader><term>Value</term><description>Behaviour</description></listheader>
///   <item><term><c>env</c> (default)</term><description>No-op pass-through; existing config is untouched.</description></item>
///   <item><term><c>file</c></term><description>Reads mounted secret files from the directory at <c>Secrets:FilePath</c>.</description></item>
///   <item><term><c>azure-keyvault</c></term><description>Not yet implemented — see extension seam below.</description></item>
///   <item><term><c>aws-secretsmanager</c></term><description>Not yet implemented — see extension seam below.</description></item>
///   <item><term><c>vault</c></term><description>Not yet implemented — see extension seam below.</description></item>
/// </list>
/// </summary>
internal static class SecretsProviderFactory
{
    private const string ProviderKey  = "Secrets:Provider";
    private const string FilePathKey  = "Secrets:FilePath";

    public static ISecretsProvider Create(IConfiguration configuration)
    {
        var providerName = configuration[ProviderKey]?.Trim().ToLowerInvariant() ?? "env";

        return providerName switch
        {
            "env"  or "" => new EnvironmentSecretsProvider(),

            "file" => CreateFileProvider(configuration),

            // ── Cloud-provider seams ───────────────────────────────────────────────────
            // To enable Azure Key Vault:
            //   1. Add NuGet: Azure.Extensions.AspNetCore.Configuration.Secrets + Azure.Identity
            //   2. Replace the throw below with:
            //      builder.Configuration.AddAzureKeyVault(
            //          new Uri(configuration["Secrets:VaultUri"]!),
            //          new DefaultAzureCredential());
            //      return new EnvironmentSecretsProvider(); // AKV is its own config source
            "azure-keyvault" => throw new NotSupportedException(
                "Secrets:Provider 'azure-keyvault' is not yet wired. " +
                "Add Azure.Extensions.AspNetCore.Configuration.Secrets + Azure.Identity, " +
                "then register AddAzureKeyVault() in SecretsProviderFactory."),

            // To enable AWS Secrets Manager:
            //   1. Add NuGet: AWSSDK.SecretsManager + Microsoft.Extensions.Configuration.Json
            //   2. Implement an AwsSecretsManagerProvider : ISecretsProvider that calls
            //      IAmazonSecretsManager.GetSecretValueAsync per secret ARN/name.
            "aws-secretsmanager" => throw new NotSupportedException(
                "Secrets:Provider 'aws-secretsmanager' is not yet wired. " +
                "Add AWSSDK.SecretsManager and implement AwsSecretsManagerProvider."),

            // To enable HashiCorp Vault:
            //   1. Add NuGet: VaultSharp
            //   2. Implement a VaultSecretsProvider : ISecretsProvider using VaultClient.V1.Secrets.
            "vault" => throw new NotSupportedException(
                "Secrets:Provider 'vault' is not yet wired. " +
                "Add VaultSharp and implement VaultSecretsProvider."),

            _ => throw new NotSupportedException(
                $"Unknown Secrets:Provider value '{providerName}'. " +
                $"Supported values: env (default), file, azure-keyvault, aws-secretsmanager, vault.")
        };
    }

    private static FileSecretsProvider CreateFileProvider(IConfiguration configuration)
    {
        var path = configuration[FilePathKey];

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                $"Secrets:Provider is 'file' but '{FilePathKey}' is not set. " +
                "Set Secrets:FilePath to the directory that contains the secret files.");

        return new FileSecretsProvider(path);
    }
}
