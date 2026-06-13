using System.Text.Json;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Worker.Services.AutoDispatch;

/// <summary>
/// Loads dispatch-mode settings (kernel.system_settings, category 'dispatch', key 'mode')
/// and resolves the effective mode per job with franchise &gt; brand &gt; platform precedence.
///
/// Policy enforced here (defence in depth alongside the dispatch.mode.manage permission):
/// only a PLATFORM-scoped row may enable 'offer_accept'. Any brand- or franchise-scoped
/// row can only narrow to 'push'. A franchise with no override inherits the platform mode.
/// </summary>
public sealed class DispatchConfig
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<Row> _rows;
    private readonly DispatchSettings _platform;

    private DispatchConfig(List<Row> rows)
    {
        _rows = rows;
        _platform = rows.FirstOrDefault(r => r.BrandId is null)?.Settings ?? new DispatchSettings();
    }

    private sealed record Row(Guid? BrandId, Guid? FranchiseId, DispatchSettings Settings);

    /// <summary>Loads all dispatch-mode rows relevant to the given brands plus the platform default.</summary>
    public static async Task<DispatchConfig> LoadAsync(
        LaundryGharDbContext db, IReadOnlyCollection<Guid> brandIds, CancellationToken ct)
    {
        var raw = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == "dispatch" && s.SettingKey == "mode" && s.Status == "active"
                     && (s.BrandId == null || brandIds.Contains(s.BrandId.Value)))
            .Select(s => new { s.BrandId, s.FranchiseId, s.SettingValue })
            .ToListAsync(ct);

        var rows = new List<Row>(raw.Count);
        foreach (var r in raw)
        {
            DispatchSettings? parsed;
            try { parsed = JsonSerializer.Deserialize<DispatchSettings>(r.SettingValue, Json); }
            catch (JsonException) { parsed = null; }
            rows.Add(new Row(r.BrandId, r.FranchiseId, parsed ?? new DispatchSettings()));
        }
        return new DispatchConfig(rows);
    }

    /// <summary>Offer-loop parameters (TTL / rounds / fan-out) come from the platform row.</summary>
    public DispatchSettings Parameters => _platform;

    /// <summary>
    /// Effective dispatch mode for a job. Franchise override (push) wins, then brand override
    /// (push), else the platform mode (the only scope that may be 'offer_accept').
    /// </summary>
    public string ResolveMode(Guid brandId, Guid? franchiseId)
    {
        if (franchiseId is not null &&
            _rows.Any(r => r.BrandId == brandId && r.FranchiseId == franchiseId))
            return DispatchSettings.ModePush;   // franchise may only narrow to push

        if (_rows.Any(r => r.BrandId == brandId && r.FranchiseId is null))
            return DispatchSettings.ModePush;   // brand override → push

        return DispatchSettings.Normalize(_platform.Mode, isPlatformScope: true);
    }
}
