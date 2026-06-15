using System.Text.Json;
using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.Settings.Queries.GetFareSettings;

public sealed record GetFareSettingsQuery : IQuery<FareSettings>;

public sealed class GetFareSettingsHandler : IQueryHandler<GetFareSettingsQuery, FareSettings>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetFareSettingsHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<FareSettings> HandleAsync(GetFareSettingsQuery query, CancellationToken ct)
    {
        var brandId = await SettingsStore.ResolveBrandIdAsync(_user, _db, ct);
        var row = await SettingsStore.FindAsync(_db, brandId, "fare", "quote", ct);
        if (row is null) return new FareSettings();
        try { return JsonSerializer.Deserialize<FareSettings>(row.SettingValue, Json) ?? new FareSettings(); }
        catch (JsonException) { return new FareSettings(); }
    }
}
