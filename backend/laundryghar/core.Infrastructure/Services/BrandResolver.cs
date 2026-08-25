using core.Application.Common.Interfaces;
using laundryghar.SharedDataModel.Persistence;
using laundryghar.Utilities.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Services;

/// <summary>
/// Resolves brand ID for anonymous public endpoints.
///
/// Resolution order (first match wins):
///   1. <b>Host header</b> → a verified row in <c>tenancy_org.brand_domains</c>. This is white-label
///      tier T2 (PLATFORM_STRATEGY.md §4.2): one deployment serves N branded domains, and the
///      hostname IS the tenant identity.
///   2. <c>X-Brand-Id</c> header (UUID) — the mobile/admin clients' explicit selector.
///   3. <c>?brandCode=</c> query parameter, resolved via the brands table.
///   4. The default brand code (<c>LG-MAIN</c>).
///
/// <para><b>Why Host outranks X-Brand-Id.</b> Both are client-supplied, but a custom domain only
/// resolves after the provider has proved ownership of it, whereas <c>X-Brand-Id</c> is an unchecked
/// assertion on these anonymous endpoints. When a request genuinely arrives on theirbrand.com, that
/// domain is the more trustworthy signal — and it is what §4.2 specifies. Hosts we do not know
/// (api.laundryghar.com, localhost, an IP) fall straight through, so every pre-existing caller keeps
/// its exact previous behaviour.</para>
///
/// <para><b>Why this is the anonymous path only.</b> For an AUTHENTICATED request the brand comes
/// from the JWT (with the X-Brand-Id override for platform admins, applied in
/// <c>TenantResolutionMiddleware</c>). Host must never override that: a signed-in user who opened
/// another provider's domain would otherwise be silently switched into that tenant.</para>
///
/// <para><b>Design note on anonymous brand resolution.</b> RLS is driven by the
/// <c>app.current_brand_id</c> session variable, which the RLS interceptor sets only when a token is
/// present. Anonymous requests have no token, so every query for a public endpoint MUST carry an
/// explicit brand predicate — this resolver supplies the id for those predicates.</para>
/// </summary>
public sealed class BrandResolver : IBrandResolver
{
    private const string DefaultBrandCode = "LG-MAIN";

    /// <summary>Bounds how long a domain change takes to go live across processes. Same trade-off,
    /// and same reasoning, as <c>TokenVersionStore</c>: a short TTL beats cross-process invalidation
    /// at this scale. Misses are cached too — the overwhelmingly common case is a host that is NOT a
    /// custom domain (our own API hostnames), and without a negative entry every such request would
    /// pay a database round-trip.</summary>
    private static readonly TimeSpan HostCacheTtl = TimeSpan.FromSeconds(30);

    private readonly ICoreDbContext _db;
    private readonly LaundryGharDbContext _raw;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BrandResolver> _logger;

    public BrandResolver(
        ICoreDbContext db,
        LaundryGharDbContext raw,
        IMemoryCache cache,
        ILogger<BrandResolver> logger)
    {
        _db     = db;
        _raw    = raw;
        _cache  = cache;
        _logger = logger;
    }

    public async Task<Guid?> ResolveAsync(HttpContext context, CancellationToken ct = default)
    {
        // 1. Host header → custom domain (verified only).
        var byHost = await ResolveByHostAsync(context.Request.Host.Host, ct);
        if (byHost is not null) return byHost;

        // 2. X-Brand-Id header (UUID) — no DB lookup needed.
        if (context.Request.Headers.TryGetValue("X-Brand-Id", out var headerVal)
            && Guid.TryParse(headerVal, out var headerGuid))
        {
            return headerGuid;
        }

        // 3. ?brandCode= query parameter, else 4. the default brand code.
        var brandCode = context.Request.Query["brandCode"].FirstOrDefault()
                     ?? DefaultBrandCode;

        var brand = await _db.Brands.IgnoreQueryFilters()
            .Where(b => b.Code == brandCode && b.DeletedAt == null)
            .Select(b => new { b.Id })
            .FirstOrDefaultAsync(ct);

        if (brand is null)
        {
            _logger.LogWarning("BrandResolver: brand code '{Code}' not found.", brandCode);
            return null;
        }

        return brand.Id;
    }

    /// <summary>
    /// Maps a hostname to its brand via <c>kernel.resolve_brand_domain</c>. That function is
    /// SECURITY DEFINER by necessity: this runs anonymously as <c>app_user</c>, and
    /// <c>brand_domains</c> is RLS-protected, so a direct read returns zero rows (with no token
    /// there is no <c>app.current_brand_id</c> to match, and bypass is off). The function grants
    /// exactly this one lookup instead of a blanket request-wide RLS bypass. It also applies the
    /// verified-only rule and the case-insensitive comparison — see migration 0003.
    /// </summary>
    private async Task<Guid?> ResolveByHostAsync(string? host, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;

        // Request.Host.Host is already port-less. Trim the FQDN root dot and normalise case so
        // "Shop.Example.", "shop.example" and "SHOP.EXAMPLE" share one cache entry.
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.Length == 0) return null;

        // Hosts that can never be a custom domain: skip the lookup and the cache entirely.
        if (normalized is "localhost" || System.Net.IPAddress.TryParse(normalized, out _))
            return null;

        var cacheKey = $"branddomain:{normalized}";
        if (_cache.TryGetValue<Guid?>(cacheKey, out var cached)) return cached;

        Guid? resolved;
        try
        {
            var rows = await _raw.Database
                .SqlQuery<Guid?>($"SELECT kernel.resolve_brand_domain({normalized}) AS \"Value\"")
                .ToListAsync(ct);
            resolved = rows.Count > 0 ? rows[0] : null;
        }
        catch (Exception ex)
        {
            // Fail through, never fail the request: an error here must degrade to the
            // header/query/default chain, which is exactly the pre-T-09 behaviour. Not cached, so
            // a transient fault does not pin the miss for the whole TTL.
            _logger.LogWarning(ex, "BrandResolver: host lookup failed for '{Host}'.", normalized);
            return null;
        }

        // Cache hits AND misses — see HostCacheTtl.
        _cache.Set(cacheKey, resolved, HostCacheTtl);
        return resolved;
    }
}
