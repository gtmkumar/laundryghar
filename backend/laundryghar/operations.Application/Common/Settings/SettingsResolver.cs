using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Common.Settings;

/// <summary>The winning setting after precedence resolution: its jsonb value, declared type,
/// and which scope level supplied it (so the UI can show "inherited from brand", etc.).</summary>
public sealed record EffectiveSetting(string Value, string DataType, string SourceScope);

/// <summary>
/// Resolves scope-aware business-rule settings from <c>kernel.system_settings</c> with the
/// precedence <b>store → franchise → brand → platform</b>. Only <c>status = 'active'</c> rows count;
/// a platform default is a row with <c>ScopeType = 'platform'</c> and <c>BrandId = null</c>.
/// Pure reads (no tracking, no writes) so it is safe to call from any handler.
/// </summary>
public static class SettingsResolver
{
    // Lower index = higher precedence. Used to pick a winner among candidate rows.
    private static readonly string[] Precedence = ["store", "franchise", "brand", "platform"];

    private static int Rank(string scopeType)
    {
        var i = Array.IndexOf(Precedence, scopeType);
        return i < 0 ? int.MaxValue : i;
    }

    /// <summary>Resolves a single (category, key) to its effective value, or null when unset at every scope.</summary>
    public static async Task<EffectiveSetting?> GetEffectiveAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        string category, string key, CancellationToken ct)
    {
        var candidates = await QueryCandidatesAsync(db, brandId, franchiseId, storeId, category, [key], ct);
        return Pick(candidates);
    }

    /// <summary>
    /// Resolves several keys of one category in a single query. Keys with no row at any scope are
    /// omitted from the result (callers treat a missing key as "unset → use fallback").
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, EffectiveSetting>> GetEffectiveBatchAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        string category, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        var candidates = await QueryCandidatesAsync(db, brandId, franchiseId, storeId, category, keys, ct);

        var result = new Dictionary<string, EffectiveSetting>();
        foreach (var group in candidates.GroupBy(c => c.SettingKey))
        {
            var winner = Pick(group);
            if (winner is not null) result[group.Key] = winner;
        }
        return result;
    }

    // ── Typed convenience readers (return null when the key is unset at every scope) ──────────

    public static async Task<decimal?> GetDecimalAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        string category, string key, CancellationToken ct)
    {
        var eff = await GetEffectiveAsync(db, brandId, franchiseId, storeId, category, key, ct);
        return eff is null ? null : SettingValueCodec.TryDecimal(eff.Value);
    }

    public static async Task<int?> GetIntAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        string category, string key, CancellationToken ct)
    {
        var eff = await GetEffectiveAsync(db, brandId, franchiseId, storeId, category, key, ct);
        return eff is null ? null : SettingValueCodec.TryInt(eff.Value);
    }

    public static async Task<bool?> GetBoolAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        string category, string key, CancellationToken ct)
    {
        var eff = await GetEffectiveAsync(db, brandId, franchiseId, storeId, category, key, ct);
        return eff is null ? null : SettingValueCodec.TryBool(eff.Value);
    }

    public static async Task<string?> GetStringAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        string category, string key, CancellationToken ct)
    {
        var eff = await GetEffectiveAsync(db, brandId, franchiseId, storeId, category, key, ct);
        return eff is null ? null : SettingValueCodec.DecodeString(eff.Value);
    }

    // ── Internals ─────────────────────────────────────────────────────────────────────────────

    private sealed record Candidate(string SettingKey, string ScopeType, string Value, string DataType);

    private static async Task<List<Candidate>> QueryCandidatesAsync(
        IOperationsDbContext db, Guid brandId, Guid? franchiseId, Guid? storeId,
        string category, IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        var keyArr = keys as string[] ?? keys.ToArray();

        // One query pulls every row that could win for these keys: the platform default, the brand
        // row, and (only when supplied) the matching franchise / store rows. A franchise-scope row
        // always has a non-null franchise_id, so a null franchiseId argument matches none of them.
        return await db.SystemSettings.AsNoTracking()
            .Where(s => s.Category == category
                     && keyArr.Contains(s.SettingKey)
                     && s.Status == "active"
                     && (
                          (s.ScopeType == "platform"  && s.BrandId == null)
                       || (s.ScopeType == "brand"     && s.BrandId == brandId)
                       || (s.ScopeType == "franchise" && s.FranchiseId == franchiseId)
                       || (s.ScopeType == "store"     && s.StoreId == storeId)
                        ))
            .Select(s => new Candidate(s.SettingKey, s.ScopeType, s.SettingValue, s.DataType))
            .ToListAsync(ct);
    }

    private static EffectiveSetting? Pick(IEnumerable<Candidate> candidates)
    {
        Candidate? best = null;
        foreach (var c in candidates)
            if (best is null || Rank(c.ScopeType) < Rank(best.ScopeType))
                best = c;
        return best is null ? null : new EffectiveSetting(best.Value, best.DataType, best.ScopeType);
    }
}
