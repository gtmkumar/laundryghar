using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace laundryghar.Utilities.Caching;

/// <summary>
/// Shared server-side output caching for GET endpoints whose response is identical across
/// all users of a tenant and changes infrequently (public CMS content, customer catalog,
/// plan listings, static config).
///
/// House pattern:
///   endpoint:  <c>group.MapGet(...).CacheSharedOutput(tag, ttl, "queryKey", …)</c>
///   mutations: <c>group.EvictOutputCacheOnWrite(tag, …)</c> on the admin group that edits
///              the underlying content — regeneration on content change; the TTL is only a
///              backstop (and the schedule-based bound for out-of-band edits).
///
/// The cache key always includes the caller's tenant identity (JWT brand/franchise/store
/// claims, the platform-admin X-Brand-Id override, or the anonymous X-Brand-Id header /
/// ?brandCode= fallback) — brand is NOT in these URLs, so URL-only keys would leak one
/// brand's content to another. Personalized endpoints must never use this: anything keyed
/// per user (orders, profile, wallet) stays uncached.
///
/// Deploy note: the store is in-process (single instance per host in docker-compose).
/// Tag eviction does not fan out across replicas — if a host is ever scaled out, swap in a
/// Redis-backed IOutputCacheStore before relying on evictions for correctness.
/// </summary>
public static class SharedOutputCache
{
    /// <summary>Registers output caching. Pair with <c>app.UseOutputCache()</c> after UseAuthorization().</summary>
    public static IServiceCollection AddSharedOutputCache(this IServiceCollection services)
        => services.AddOutputCache();

    /// <summary>
    /// Caches this GET endpoint's 200 responses for <paramref name="ttl"/>, varying by the
    /// caller's tenant identity plus the declared query keys (declare every query parameter
    /// the handler reads — undeclared ones are ignored by the key and would bleed responses
    /// across values). Works on authenticated endpoints: authorization still runs on every
    /// request; only the endpoint execution is skipped on a hit.
    /// </summary>
    public static RouteHandlerBuilder CacheSharedOutput(
        this RouteHandlerBuilder endpoint,
        string tag,
        TimeSpan ttl,
        params string[] varyByQuery)
        => endpoint.CacheOutput(b => b
            .AddPolicy<CacheSharedBasePolicy>()
            .Expire(ttl)
            .SetVaryByQuery(varyByQuery)
            .SetVaryByHeader("X-Brand-Id")
            .VaryByValue(TenantKey)
            .Tag(tag));

    /// <summary>
    /// After any successful non-GET request in this group, evicts the given cache tags so the
    /// next read regenerates. Attach to the admin group that mutates the cached content.
    /// </summary>
    public static RouteGroupBuilder EvictOutputCacheOnWrite(
        this RouteGroupBuilder group,
        params string[] tags)
        => (RouteGroupBuilder)group.AddEndpointFilter(async (ctx, next) =>
        {
            var result = await next(ctx);
            var method = ctx.HttpContext.Request.Method;
            if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
            {
                var store = ctx.HttpContext.RequestServices.GetRequiredService<IOutputCacheStore>();
                foreach (var tag in tags)
                    await store.EvictByTagAsync(tag, ctx.HttpContext.RequestAborted);
            }
            return result;
        });

    /// <summary>
    /// Tenant discriminator for the cache key. Order matches how the request pipeline resolves
    /// brand: platform-admin override, then JWT claims, then the anonymous header/query
    /// fallbacks used by IBrandResolver. Franchise/store claims are folded in defensively so a
    /// store-scoped token can never share an entry with a brand-wide one.
    /// </summary>
    private static ValueTask<KeyValuePair<string, string>> TenantKey(HttpContext ctx, CancellationToken _)
    {
        var brand = ctx.Items["brand_id_override"]?.ToString()
            ?? ctx.User.FindFirstValue("brand_id")
            ?? ctx.Request.Headers["X-Brand-Id"].FirstOrDefault()
            ?? ctx.Request.Query["brandCode"].FirstOrDefault()
            ?? "-";
        var franchise = ctx.User.FindFirstValue("franchise_id") ?? "-";
        var store = ctx.User.FindFirstValue("store_id") ?? "-";
        return ValueTask.FromResult(
            new KeyValuePair<string, string>("tenant", $"{brand}|{franchise}|{store}"));
    }
}

/// <summary>
/// Base policy for shared output caching. Mirrors the framework default policy except it
/// permits caching requests that carry an Authorization header — safe ONLY because
/// <see cref="SharedOutputCache.CacheSharedOutput"/> always folds the token's tenant identity
/// into the cache key, and the endpoints opting in return tenant-wide (not per-user) data.
/// Responses are stored only for 200s without Set-Cookie.
/// </summary>
public sealed class CacheSharedBasePolicy : IOutputCachePolicy
{
    ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken ct)
    {
        var method = context.HttpContext.Request.Method;
        var attempt = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = attempt;
        context.AllowCacheStorage = attempt;
        context.AllowLocking = true;
        return ValueTask.CompletedTask;
    }

    ValueTask IOutputCachePolicy.ServeFromCacheAsync(OutputCacheContext context, CancellationToken ct)
        => ValueTask.CompletedTask;

    ValueTask IOutputCachePolicy.ServeResponseAsync(OutputCacheContext context, CancellationToken ct)
    {
        var response = context.HttpContext.Response;
        if (!string.IsNullOrEmpty(response.Headers.SetCookie)
            || response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
        }
        return ValueTask.CompletedTask;
    }
}
