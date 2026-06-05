using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Engagement.Application.Cms.Queries;
using MediatR;

namespace laundryghar.Engagement.Endpoints;

/// <summary>
/// Anonymous public endpoints consumed by mobile/web apps before the user logs in.
/// Brand is resolved from:
///   1. X-Brand-Id header (UUID) — direct, no DB lookup.
///   2. ?brandCode= query param — DB lookup.
///   3. Default "LG-MAIN".
///
/// RLS cannot be relied upon here (no auth token → no SET LOCAL app.current_brand_id).
/// All LINQ queries include explicit .Where(brandId) predicates via the public query variants.
/// </summary>
public static class PublicEngagementEndpoints
{
    public static RouteGroupBuilder MapPublicEngagementEndpoints(this RouteGroupBuilder group)
    {
        var g = group.MapGroup("/public").WithTags("Public - CMS");
        // Explicitly allow anonymous access — no RequireAuthorization()
        g.AllowAnonymous();

        // GET /api/v1/public/onboarding-slides?appType=customer
        g.MapGet("/onboarding-slides", async (
            HttpContext ctx,
            IBrandResolver brandResolver,
            ISender sender,
            CancellationToken ct,
            string appType = "customer") =>
        {
            var brandId = await brandResolver.ResolveAsync(ctx, ct);
            if (brandId is null)
                return Results.NotFound(new { error = "Brand not found." });

            if (!IsValidAppType(appType))
                return Results.BadRequest(new { error = "appType must be one of: customer, rider, staff, pos" });

            var slides = await sender.Send(new GetPublicOnboardingSlidesQuery(brandId.Value, appType), ct);
            return Results.Ok(new { status = true, data = slides });
        });

        // GET /api/v1/public/app-config?platform=android
        g.MapGet("/app-config", async (
            HttpContext ctx,
            IBrandResolver brandResolver,
            ISender sender,
            CancellationToken ct,
            string platform = "android") =>
        {
            var brandId = await brandResolver.ResolveAsync(ctx, ct);
            if (brandId is null)
                return Results.NotFound(new { error = "Brand not found." });

            if (!IsValidPlatform(platform))
                return Results.BadRequest(new { error = "platform must be one of: android, ios, web" });

            var configs = await sender.Send(new GetPublicAppConfigQuery(brandId.Value, platform), ct);
            return Results.Ok(new { status = true, data = configs });
        });

        // GET /api/v1/public/banners?placement=home_top
        g.MapGet("/banners", async (
            HttpContext ctx,
            IBrandResolver brandResolver,
            ISender sender,
            CancellationToken ct,
            string? placement = null) =>
        {
            var brandId = await brandResolver.ResolveAsync(ctx, ct);
            if (brandId is null)
                return Results.NotFound(new { error = "Brand not found." });

            if (!string.IsNullOrEmpty(placement) && !IsValidPlacement(placement))
                return Results.BadRequest(new { error = "placement must be one of: home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile" });

            var banners = await sender.Send(new GetPublicBannersQuery(brandId.Value, placement), ct);
            return Results.Ok(new { status = true, data = banners });
        });

        return group;
    }

    private static readonly string[] ValidAppTypes  = ["customer", "rider", "staff", "pos"];
    private static readonly string[] ValidPlatforms = ["android", "ios", "web"];
    private static readonly string[] ValidPlacements =
        ["home_top", "home_middle", "home_bottom", "services_top", "cart_top", "order_success", "profile"];

    private static bool IsValidAppType(string v) => ValidAppTypes.Contains(v, StringComparer.OrdinalIgnoreCase);
    private static bool IsValidPlatform(string v) => ValidPlatforms.Contains(v, StringComparer.OrdinalIgnoreCase);
    private static bool IsValidPlacement(string v) => ValidPlacements.Contains(v, StringComparer.OrdinalIgnoreCase);
}
