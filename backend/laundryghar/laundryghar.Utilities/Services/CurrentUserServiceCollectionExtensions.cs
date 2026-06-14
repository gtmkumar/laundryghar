using Microsoft.Extensions.DependencyInjection;

namespace laundryghar.Utilities.Services;

public static class CurrentUserServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ICurrentUser"/> backed by the HTTP request principal.</summary>
    public static IServiceCollection AddCurrentUser(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        return services;
    }
}
