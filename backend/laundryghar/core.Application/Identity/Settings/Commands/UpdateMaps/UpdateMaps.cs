using core.Application.Common.Interfaces;
using core.Application.Identity.Settings.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Commands.UpdateMaps;

public sealed record UpdateMapsCommand(UpdateMapsSettingsRequest Request) : ICommand<MapsSettingsView>;

public sealed class UpdateMapsHandler : ICommandHandler<UpdateMapsCommand, MapsSettingsView>
{
    private static readonly HashSet<string> Providers = new(StringComparer.OrdinalIgnoreCase) { "osm", "google", "mapbox" };
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateMapsHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<MapsSettingsView> HandleAsync(UpdateMapsCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var provider = r.Provider?.Trim().ToLowerInvariant() ?? "osm";
        if (!Providers.Contains(provider))
            throw new ValidationException(new Dictionary<string, string[]>
                { ["provider"] = ["Provider must be 'osm', 'google' or 'mapbox'."] });

        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);

        // Preserve a stored key when the client sends a blank one (keys aren't
        // re-entered on every save). Trim to null so blanks don't masquerade as set.
        var existing = await SettingsStore.LoadMapsAsync(_db, brandId, ct);
        string? Keep(string? incoming, string? stored)
            => string.IsNullOrWhiteSpace(incoming) ? stored : incoming.Trim();

        var value = new MapsSettings
        {
            Provider = provider,
            GoogleApiKey = Keep(r.GoogleApiKey, existing.GoogleApiKey),
            MapboxToken  = Keep(r.MapboxToken,  existing.MapboxToken),
        };

        // A keyed provider needs its key — guard so the map never selects a broken provider.
        if (provider == "google" && string.IsNullOrWhiteSpace(value.GoogleApiKey))
            throw new ValidationException(new Dictionary<string, string[]> { ["googleApiKey"] = ["A Google Maps API key is required to use Google."] });
        if (provider == "mapbox" && string.IsNullOrWhiteSpace(value.MapboxToken))
            throw new ValidationException(new Dictionary<string, string[]> { ["mapboxToken"] = ["A Mapbox access token is required to use Mapbox."] });

        await SettingsStore.UpsertAsync(_db, brandId, "maps", "provider", value, isEncrypted: false, _user.UserId, ct);
        return new MapsSettingsView(value.Provider, value.GoogleApiKey, value.MapboxToken);
    }
}
