using System.Text.Json;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace laundryghar.Utilities.Auth.Audit;

/// <summary>
/// Systematic audit trail (docs/rbac.md §8/§12): writes an identity_access.audit_logs row for every
/// Added/Modified/Deleted entity in the in-flight SaveChanges — EXCEPT a denylist (the recursion
/// guard + the three existing narrow histories + high-volume telemetry + secret-bearing rows).
///
/// Runs on the single physical LaundryGharDbContext (all 3 split facades delegate to it) so it fires
/// ONCE per physical save regardless of which handler/context produced the change. It is FAIL-OPEN:
/// any error building audit rows is logged and swallowed so a broken audit path can never roll back a
/// real business write. brand_id is stamped from ICurrentTenant (see <see cref="AuditContext.Fill"/>)
/// so the audit INSERT's RLS WITH CHECK passes whenever the business write did.
///
/// Registered Scoped (per-request, like RlsConnectionInterceptor) so it carries the request's own
/// ICurrentTenant/ICurrentUser snapshot — never leaking one tenant's actor into another's writes.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // CLR type names never audited: recursion guard, the 3 narrow histories (kept additive), and
    // high-volume/secret telemetry already captured elsewhere or not worth 7-year retention.
    private static readonly HashSet<string> Denylist = new(StringComparer.Ordinal)
    {
        nameof(AuditLog),
        "LoginHistory", "OrderStatusHistory", "PricingChangeLog",
        "NotificationOutbox", "NotificationLog", "WhatsAppMessageLog", "PushToken",
        "RiderLocationPing", "OtpCode", "RefreshToken", "PasswordReset",
        "OAuthAuthorizationCode",
    };

    private readonly ICurrentTenant _tenant;
    private readonly ICurrentUser _user;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditSaveChangesInterceptor> _logger;

    public AuditSaveChangesInterceptor(
        ICurrentTenant tenant,
        ICurrentUser user,
        IHttpContextAccessor http,
        ILogger<AuditSaveChangesInterceptor> logger)
    {
        _tenant = tenant;
        _user   = user;
        _http   = http;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        TryCapture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        TryCapture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void TryCapture(DbContext? ctx)
    {
        if (ctx is null) return;
        try
        {
            var http = _http.HttpContext;
            var rows = new List<AuditLog>();

            // Snapshot entries first — Adding audit rows below mutates the change tracker.
            foreach (var entry in ctx.ChangeTracker.Entries().ToList())
            {
                if (entry.Entity is AuditLog) continue; // recursion guard
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
                if (Denylist.Contains(entry.Entity.GetType().Name)) continue;

                try
                {
                    var row = BuildRow(entry);
                    if (row is null) continue;
                    AuditContext.Fill(row, _tenant, _user, http);
                    rows.Add(row);
                }
                catch (Exception ex)
                {
                    // Per-entity fail-open: one un-serializable entity must not drop the batch.
                    _logger.LogWarning(ex, "Audit capture skipped for {Entity}", entry.Entity.GetType().Name);
                }
            }

            if (rows.Count > 0) ctx.Set<AuditLog>().AddRange(rows);
        }
        catch (Exception ex)
        {
            // Fail-open: never let auditing roll back the business write.
            _logger.LogError(ex, "Audit trail capture failed; business write proceeds un-audited");
        }
    }

    private static AuditLog? BuildRow(EntityEntry entry)
    {
        var table = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name;
        var (verb, isModified) = entry.State switch
        {
            EntityState.Added    => ("created", false),
            EntityState.Deleted  => ("deleted", false),
            _                    => ("updated", true),
        };

        string[]? changedFields = null;
        string? oldJson = null, newJson = null;

        if (entry.State == EntityState.Added)
        {
            newJson = Serialize(Values(entry, current: true));
        }
        else if (entry.State == EntityState.Deleted)
        {
            oldJson = Serialize(Values(entry, current: false));
        }
        else // Modified
        {
            var changed = entry.Properties.Where(p => p.IsModified).ToList();
            if (changed.Count == 0) return null; // no real column change (shadow/no-op)
            changedFields = changed.Select(p => p.Metadata.Name).ToArray();
            oldJson = Serialize(changed.ToDictionary(p => p.Metadata.Name, p => Redact(p.Metadata.Name, p.OriginalValue)));
            newJson = Serialize(changed.ToDictionary(p => p.Metadata.Name, p => Redact(p.Metadata.Name, p.CurrentValue)));
        }

        var log = new AuditLog
        {
            Id            = Guid.NewGuid(),
            Action        = Trunc($"{table}.{verb}", 100),
            ResourceType  = Trunc(table, 50),
            ResourceId    = SingleGuidKey(entry),
            OldValues     = oldJson,
            NewValues     = newJson,
            ChangedFields = changedFields,
            Success       = true,
        };
        return log;
    }

    private static Dictionary<string, object?> Values(EntityEntry entry, bool current) =>
        entry.Properties.ToDictionary(
            p => p.Metadata.Name,
            p => Redact(p.Metadata.Name, current ? p.CurrentValue : p.OriginalValue));

    private static object? Redact(string name, object? value)
        => AuditContext.IsSecret(name) ? "[redacted]" : Sanitize(value);

    // Reduce EF/CLR values to JSON-safe primitives (avoid serializer throws on geometry, byte[], etc.).
    private static object? Sanitize(object? v)
    {
        if (v is null) return null;
        var t = v.GetType();
        if (v is string or bool or Guid or DateTime or DateTimeOffset or TimeSpan or decimal || t.IsPrimitive) return v;
        if (t.IsEnum) return v.ToString();
        if (v is byte[] bytes) return $"byte[{bytes.Length}]";
        return v.ToString();
    }

    private static Guid? SingleGuidKey(EntityEntry entry)
    {
        var pk = entry.Metadata.FindPrimaryKey();
        if (pk is null || pk.Properties.Count != 1) return null;
        return entry.Property(pk.Properties[0].Name).CurrentValue is Guid g ? g : null;
    }

    private static string Serialize(IReadOnlyDictionary<string, object?> map) => JsonSerializer.Serialize(map, Json);
    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}
