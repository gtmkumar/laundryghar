using Microsoft.Extensions.Configuration;

namespace laundryghar.ServiceDefaults.Storage;

/// <summary>
/// Reads <c>Storage:Provider</c> from configuration and selects the matching
/// <see cref="IFileStorageProvider"/> registration.
///
/// <para><b>Supported values for <c>Storage:Provider</c></b></para>
/// <list type="table">
///   <listheader><term>Value</term><description>Behaviour</description></listheader>
///   <item><term><c>local</c> (default)</term>
///     <description>Writes files to disk under <c>Storage:Local:RootPath</c>. Safe for Development.</description>
///   </item>
///   <item><term><c>s3</c></term>
///     <description>Not yet wired — throws <see cref="NotSupportedException"/> with instructions.</description>
///   </item>
///   <item><term><c>azure-blob</c></term>
///     <description>Not yet wired — throws <see cref="NotSupportedException"/> with instructions.</description>
///   </item>
/// </list>
///
/// Called automatically by <c>AddServiceDefaults()</c> via <c>AddFileStorage()</c>.
/// No per-service code is needed.
/// </summary>
internal static class FileStorageProviderFactory
{
    private const string ProviderKey = "Storage:Provider";

    public static string ResolveProviderName(IConfiguration configuration)
    {
        var name = configuration[ProviderKey]?.Trim().ToLowerInvariant() ?? "local";

        return name switch
        {
            "local" or "" => "local",

            // ── Cloud provider seams ──────────────────────────────────────────────────────
            // To enable S3:
            //   1. Add NuGet: AWSSDK.S3 (or AWSSDK.Extensions.NETCore.Setup for DI).
            //   2. Create S3FileStorageProvider : IFileStorageProvider.
            //   3. Bind S3StorageOptions from config (Storage:S3:Bucket, Storage:S3:Region).
            //   4. Register it in StorageExtensions.AddFileStorage() and remove this throw.
            "s3" => throw new NotSupportedException(
                "Storage:Provider 's3' is not yet wired. " +
                "Add AWSSDK.S3, create S3FileStorageProvider, and register it in StorageExtensions."),

            // To enable Azure Blob Storage:
            //   1. Add NuGet: Azure.Storage.Blobs + Azure.Identity.
            //   2. Create AzureBlobStorageProvider : IFileStorageProvider.
            //   3. Bind AzureBlobStorageOptions from config (Storage:AzureBlob:ConnectionString, :ContainerName).
            //   4. Register it in StorageExtensions.AddFileStorage() and remove this throw.
            "azure-blob" => throw new NotSupportedException(
                "Storage:Provider 'azure-blob' is not yet wired. " +
                "Add Azure.Storage.Blobs, create AzureBlobStorageProvider, and register it in StorageExtensions."),

            _ => throw new NotSupportedException(
                $"Unknown Storage:Provider value '{name}'. " +
                "Supported values: local (default), s3, azure-blob.")
        };
    }
}
