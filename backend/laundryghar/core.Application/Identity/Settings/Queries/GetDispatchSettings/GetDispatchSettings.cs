using System.Text.Json;
using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;

namespace core.Application.Identity.Settings.Queries.GetDispatchSettings;

public sealed record GetDispatchSettingsQuery : IQuery<DispatchSettings>;

public sealed class GetDispatchSettingsHandler : IQueryHandler<GetDispatchSettingsQuery, DispatchSettings>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ICoreDbContext _db;

    public GetDispatchSettingsHandler(ICoreDbContext db) => _db = db;

    public async Task<DispatchSettings> HandleAsync(GetDispatchSettingsQuery query, CancellationToken ct)
    {
        // The mode lives at platform scope (brand rows may only narrow to push).
        var row = await SettingsStore.FindAsync(_db, brandId: null, "dispatch", "mode", ct);
        if (row is null) return new DispatchSettings();
        try { return JsonSerializer.Deserialize<DispatchSettings>(row.SettingValue, Json) ?? new DispatchSettings(); }
        catch (JsonException) { return new DispatchSettings(); }
    }
}
