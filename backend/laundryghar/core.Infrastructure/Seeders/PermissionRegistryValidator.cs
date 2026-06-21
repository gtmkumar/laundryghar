using laundryghar.SharedDataModel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Seeders;

/// <summary>
/// Read-only invariants check over the permission registry (modules ↔ permissions). Logs
/// drift so it's caught early; never throws. Addresses the "canonical registry" gap (#4):
/// it makes the registry's consistency — and the known tag overlaps (e.g. the `orders` tag
/// shared by the `orders` and `pos` modules) — visible at boot instead of silent.
/// A full single-source-of-truth refactor would remove the overlaps; this surfaces them safely.
/// </summary>
public static class PermissionRegistryValidator
{
    public static async Task ValidateAndLogAsync(LaundryGharDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var modules = await db.Modules.AsNoTracking()
                .Select(m => new { m.Key, m.RequiredPermission, m.PermissionModules })
                .ToListAsync(ct);
            var permCodes = await db.Permissions.AsNoTracking().Select(p => p.Code).ToListAsync(ct);
            var codeSet = permCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 1. Every module's required_permission must be a real permission code.
            var missing = modules
                .Where(m => !string.IsNullOrEmpty(m.RequiredPermission) && !codeSet.Contains(m.RequiredPermission!))
                .Select(m => $"{m.Key}→{m.RequiredPermission}")
                .ToList();
            if (missing.Count > 0)
                logger.LogWarning("Permission registry: {Count} module(s) require a permission code that does not exist: {List}",
                    missing.Count, string.Join(", ", missing));

            // 2. Permission tags claimed by more than one module → entitlement/permission overlap.
            var tagOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in modules)
                foreach (var tag in m.PermissionModules ?? [])
                    (tagOwners.TryGetValue(tag, out var l) ? l : tagOwners[tag] = []).Add(m.Key);
            var overlaps = tagOwners.Where(kv => kv.Value.Count > 1)
                .Select(kv => $"{kv.Key}∈[{string.Join(",", kv.Value)}]").ToList();
            if (overlaps.Count > 0)
                logger.LogInformation("Permission registry: {Count} permission tag(s) appear in multiple modules' permission_modules (matrix UI grouping only — entitlement uses the canonical module_key, so this no longer affects revocation): {List}",
                    overlaps.Count, string.Join("; ", overlaps));

            // 3. Permissions with no canonical owning module (module_key) — entitlement
            //    never filters these out (fail-open); surfaced so they can be assigned.
            var orphans = await db.Permissions.AsNoTracking()
                .Where(p => p.ModuleKey == null)
                .Select(p => p.Code).ToListAsync(ct);
            if (orphans.Count > 0)
                logger.LogWarning("Permission registry: {Count} permission(s) have no canonical module_key (always allowed by entitlement): {List}",
                    orphans.Count, string.Join(", ", orphans));

            if (missing.Count == 0)
                logger.LogInformation("Permission registry: OK — every module's required_permission resolves ({Modules} modules, {Perms} permissions, {Orphans} unowned).",
                    modules.Count, codeSet.Count, orphans.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Permission registry validation skipped (non-fatal).");
        }
    }
}
