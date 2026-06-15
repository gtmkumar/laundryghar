using core.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.AccessControl;

/// <summary>
/// Data-driven module taxonomy loaded from <c>identity_access.modules</c>.
/// Provides the matrix rows (ordered) and maps a permission's raw module onto
/// the UI module rows it belongs to (a raw module may map to several rows —
/// e.g. <c>orders</c> → Orders + POS, <c>analytics</c> → Analytics + Dashboard).
/// </summary>
public sealed class ModuleMatrix
{
    public IReadOnlyList<(string Key, string Label)> Rows { get; }
    private readonly Dictionary<string, List<string>> _rawToUi; // raw permission module → UI row keys
    private static readonly List<string> SettingsFallback = ["settings"];

    private ModuleMatrix(IReadOnlyList<(string, string)> rows, Dictionary<string, List<string>> rawToUi)
    {
        Rows = rows;
        _rawToUi = rawToUi;
    }

    public static async Task<ModuleMatrix> LoadAsync(ICoreDbContext db, CancellationToken ct)
    {
        var mods = await db.Modules.AsNoTracking()
            .Where(m => m.ShowInMatrix && m.Status == "active")
            .OrderBy(m => m.MatrixOrder)
            .Select(m => new { m.Key, m.Label, m.PermissionModules })
            .ToListAsync(ct);

        var rows = mods.Select(m => (m.Key, m.Label)).ToList();
        var map = new Dictionary<string, List<string>>();
        foreach (var m in mods)
            foreach (var raw in m.PermissionModules)
            {
                if (!map.TryGetValue(raw, out var list)) { list = []; map[raw] = list; }
                if (!list.Contains(m.Key)) list.Add(m.Key);
            }

        return new ModuleMatrix(rows, map);
    }

    /// <summary>The matrix cells ("module:action") a permission satisfies.</summary>
    public IEnumerable<string> CellsFor(string module, string action)
    {
        var uiKeys = _rawToUi.TryGetValue(module, out var keys) ? keys : SettingsFallback;
        foreach (var ui in uiKeys)
        {
            // Dashboard is a read-only surface — only ever a "view" cell.
            if (ui == "dashboard")
            {
                if (PermissionMatrix.Columns(action).Contains("view")) yield return "dashboard:view";
                continue;
            }
            foreach (var col in PermissionMatrix.Columns(action))
                yield return $"{ui}:{col}";
        }
    }
}
