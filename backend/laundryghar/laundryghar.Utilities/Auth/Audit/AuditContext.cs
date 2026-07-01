using System.Security.Claims;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Http;

namespace laundryghar.Utilities.Auth.Audit;

/// <summary>
/// Shared helpers used by BOTH the <see cref="AuditSaveChangesInterceptor"/> (entity-change audit)
/// and <see cref="IAuditWriter"/> (explicit command/denial audit) so every row is stamped identically.
/// </summary>
internal static class AuditContext
{
    /// <summary>Property-name fragments whose values must NEVER land in the 7-year audit table.
    /// Matched case-insensitively against the scalar property name.</summary>
    private static readonly string[] SecretMarkers =
        ["password", "secret", "token", "hash", "salt", "privatekey", "apikey", "cvv", "otp"];

    public static bool IsSecret(string propertyName)
        => SecretMarkers.Any(m => propertyName.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>Redact a single value by property name (secrets/PII → "[redacted]").</summary>
    public static object? Redact(string propertyName, object? value)
        => value is not null && IsSecret(propertyName) ? "[redacted]" : value;

    /// <summary>
    /// Stamp actor / tenant / request context + timestamps on a row. brand_id is taken from
    /// <see cref="ICurrentTenant"/> (NOT the mutated entity) so it equals the RLS session var
    /// (app.current_brand_id) — the audit INSERT's WITH CHECK then passes by construction whenever
    /// the business write in the same transaction passed. occurred_at is UtcNow so the row always
    /// lands in the always-present current-month partition.
    /// </summary>
    public static void Fill(AuditLog log, ICurrentTenant tenant, ICurrentUser user, HttpContext? http)
    {
        var now = DateTimeOffset.UtcNow;
        log.OccurredAt = now;
        log.CreatedAt  = now;

        // Tenant — the RLS invariant above.
        log.BrandId     = tenant.BrandId;
        log.FranchiseId = tenant.FranchiseId;
        log.StoreId     = tenant.StoreId;

        // Actor. Check token_use FIRST: a customer token's `sub` is a customer id (not a user),
        // and ICurrentUser.UserId would otherwise surface it in the user branch.
        var principal = http?.User;
        var tokenUse = principal?.FindFirstValue("token_use");
        if (tokenUse is "customer" or "customer_mcp")
        {
            log.ActorType = "customer";
            if (Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var cid))
                log.ActorCustomerId ??= cid;
            log.ActorDisplay ??= principal?.FindFirstValue("phone");
        }
        else if (user.UserId is { } uid)
        {
            log.ActorType = "user";
            log.ActorUserId = uid;
            log.CreatedBy   = uid;
            log.ActorDisplay ??= user.Email ?? user.Phone;
        }
        else
        {
            // No principal → a background worker / seed / job path.
            log.ActorType = http is null ? "system" : "api";
        }

        // Request context (best-effort; null on non-HTTP paths).
        if (http is not null)
        {
            log.IpAddress ??= http.Connection?.RemoteIpAddress;
            var ua = http.Request?.Headers.UserAgent.ToString();
            if (!string.IsNullOrEmpty(ua)) log.UserAgent ??= ua.Length > 1000 ? ua[..1000] : ua;
            if (Guid.TryParse(http.TraceIdentifier, out var reqId)) log.RequestId ??= reqId;
        }
        if (System.Diagnostics.Activity.Current is { } act
            && Guid.TryParse(act.TraceId.ToString(), out var corr))
            log.CorrelationId ??= corr;
    }
}
