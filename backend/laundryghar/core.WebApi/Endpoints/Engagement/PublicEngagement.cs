using core.Application.Engagement.Cms.AppBanners.Queries.GetPublicBanners;
using core.Application.Engagement.Cms.MobileAppConfigs.Queries.GetPublicAppConfig;
using core.Application.Engagement.Cms.OnboardingSlides.Queries.GetPublicOnboardingSlides;
using laundryghar.Utilities.Caching;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Engagement;

/// <summary>
/// Output-cache tags for CMS content. A tag couples the cached public read to the admin
/// group that edits it (<c>EvictOutputCacheOnWrite</c>) so content changes regenerate
/// the cached response immediately; the per-endpoint TTL is only a backstop.
/// </summary>
public static class CmsCacheTags
{
    public const string OnboardingSlides = "cms:onboarding-slides";
    public const string AppConfig = "cms:app-config";
    public const string Banners = "cms:banners";
}

/// <summary>
/// Anonymous public endpoints consumed by mobile/web apps before the user logs in.
/// Brand is resolved from:
///   1. X-Brand-Id header (UUID) — direct, no DB lookup.
///   2. ?brandCode= query param — DB lookup.
///   3. Default "LG-MAIN".
///
/// RLS cannot be relied upon here (no auth token → no SET LOCAL app.current_brand_id).
/// All LINQ queries include explicit .Where(brandId) predicates via the public query variants.
///
/// NOTE: the success/error response shapes here (<c>new { status = true, data }</c> /
/// <c>new { error = "..." }</c>) are public client contracts and intentionally do NOT use the
/// SingleResponse/PaginatedListResponse envelopes used by admin endpoints.
/// </summary>
public class PublicEngagement : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/public";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Public - CMS");
        // Explicitly allow anonymous access — no RequireAuthorization()
        group.AllowAnonymous();

        // Output-cached: identical for every user of a brand and edited rarely (admin CMS).
        // Keyed by tenant + the query params each handler reads; evicted by the matching
        // admin groups on write. TTLs are regenerate-on-schedule backstops only.
        group.MapGet(OnboardingSlides, "onboarding-slides")
            .CacheSharedOutput(CmsCacheTags.OnboardingSlides, TimeSpan.FromMinutes(30), "appType", "brandCode");
        group.MapGet(AppConfig, "app-config")
            .CacheSharedOutput(CmsCacheTags.AppConfig, TimeSpan.FromMinutes(10), "platform", "brandCode");
        group.MapGet(Banners, "banners")
            .CacheSharedOutput(CmsCacheTags.Banners, TimeSpan.FromMinutes(10), "placement", "brandCode");
    }

    // GET /api/v1/public/onboarding-slides?appType=customer
    public static async Task<IResult> OnboardingSlides(
        HttpContext ctx,
        IBrandResolver brandResolver,
        IDispatcher dispatcher,
        CancellationToken ct,
        string appType = "customer")
    {
        var brandId = await brandResolver.ResolveAsync(ctx, ct);
        if (brandId is null)
            return Results.NotFound(new { error = "Brand not found." });

        if (!IsValidAppType(appType))
            return Results.BadRequest(new { error = "appType must be one of: customer, rider, staff, pos" });

        var data = await dispatcher.QueryAsync(new GetPublicOnboardingSlidesQuery(brandId.Value, appType), ct);
        return Results.Ok(new { status = true, data });
    }

    // GET /api/v1/public/app-config?platform=android
    public static async Task<IResult> AppConfig(
        HttpContext ctx,
        IBrandResolver brandResolver,
        IDispatcher dispatcher,
        CancellationToken ct,
        string platform = "android")
    {
        var brandId = await brandResolver.ResolveAsync(ctx, ct);
        if (brandId is null)
            return Results.NotFound(new { error = "Brand not found." });

        if (!IsValidPlatform(platform))
            return Results.BadRequest(new { error = "platform must be one of: android, ios, web" });

        var data = await dispatcher.QueryAsync(new GetPublicAppConfigQuery(brandId.Value, platform), ct);
        return Results.Ok(new { status = true, data });
    }

    // GET /api/v1/public/banners?placement=home_top
    public static async Task<IResult> Banners(
        HttpContext ctx,
        IBrandResolver brandResolver,
        IDispatcher dispatcher,
        CancellationToken ct,
        string? placement = null)
    {
        var brandId = await brandResolver.ResolveAsync(ctx, ct);
        if (brandId is null)
            return Results.NotFound(new { error = "Brand not found." });

        if (!string.IsNullOrEmpty(placement) && !IsValidPlacement(placement))
            return Results.BadRequest(new { error = "placement must be one of: home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile" });

        var data = await dispatcher.QueryAsync(new GetPublicBannersQuery(brandId.Value, placement), ct);
        return Results.Ok(new { status = true, data });
    }

    private static readonly string[] ValidAppTypes  = ["customer", "rider", "staff", "pos"];
    private static readonly string[] ValidPlatforms = ["android", "ios", "web"];
    private static readonly string[] ValidPlacements =
        ["home_top", "home_middle", "home_bottom", "services_top", "cart_top", "order_success", "profile"];

    private static bool IsValidAppType(string v) => ValidAppTypes.Contains(v, StringComparer.OrdinalIgnoreCase);
    private static bool IsValidPlatform(string v) => ValidPlatforms.Contains(v, StringComparer.OrdinalIgnoreCase);
    private static bool IsValidPlacement(string v) => ValidPlacements.Contains(v, StringComparer.OrdinalIgnoreCase);
}
