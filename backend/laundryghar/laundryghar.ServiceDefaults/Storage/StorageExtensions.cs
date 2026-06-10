using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace laundryghar.ServiceDefaults.Storage;

/// <summary>
/// Registers the <see cref="IFileStorageProvider"/> implementation chosen by
/// <c>Storage:Provider</c> into the DI container.
///
/// Called automatically by <c>AddServiceDefaults()</c> — no per-service wiring is required.
/// </summary>
public static class StorageExtensions
{
    /// <summary>
    /// Adds <see cref="IFileStorageProvider"/> to the service collection.
    /// The implementation is selected from <c>Storage:Provider</c> in configuration.
    /// </summary>
    public static TBuilder AddFileStorage<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var providerName = FileStorageProviderFactory.ResolveProviderName(builder.Configuration);

        if (providerName == "local")
        {
            builder.Services.Configure<LocalStorageOptions>(
                builder.Configuration.GetSection("Storage:Local"));
            builder.Services.AddSingleton<IFileStorageProvider, LocalFileStorageProvider>();
        }

        // Cloud provider registrations go here once wired (see FileStorageProviderFactory seams).

        return builder;
    }
}
