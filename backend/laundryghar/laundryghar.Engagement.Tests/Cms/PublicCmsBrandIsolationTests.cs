using laundryghar.Engagement.Application.Cms.Queries;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.SharedDataModel.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace laundryghar.Engagement.Tests.Cms;

/// <summary>
/// SEC-3 regression tests for the anonymous public CMS lane in laundryghar.Core.
///
/// Part 1 — content isolation: the public CMS query handlers carry an EXPLICIT
/// .Where(brandId) predicate, so even with RLS bypassed for these routes a caller only ever
/// sees the requested brand's content. We prove brand A's request returns A's banners only,
/// and a brand with no rows returns nothing (never another tenant's rows).
///
/// Part 2 — narrowed bypass allow-list: the blanket "/api/v1/public" prefix bypass was
/// replaced with an exact-route allow-list so a FUTURE /api/v1/public/* route cannot
/// auto-bypass RLS. We pin that the 3 known routes match and arbitrary public paths do not.
/// (The predicate mirrors Program.cs IsAllowlistedPublicCmsPath, which is a file-local static.)
/// </summary>
public sealed class PublicCmsBrandIsolationTests
{
    private static readonly Guid BrandA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BrandB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static LaundryGharDbContext NewDb()
    {
        var opts = new DbContextOptionsBuilder<LaundryGharDbContext>()
            .UseInMemoryDatabase($"public-cms-{Guid.NewGuid()}")
            .EnableServiceProviderCaching(false)
            .Options;
        return new LaundryGharDbContext(opts);
    }

    private static AppBanner Banner(Guid brandId, string placement) => new()
    {
        Id                = Guid.NewGuid(),
        BrandId           = brandId,
        AppType           = "customer",
        Placement         = placement,
        TitleLocalized    = "{}",
        SubtitleLocalized = "{}",
        ImageUrl          = "https://cdn/banner.png",
        DisplayOrder      = 0,
        IsActive          = true,
        Status            = "active",
        CreatedAt         = DateTimeOffset.UtcNow,
        UpdatedAt         = DateTimeOffset.UtcNow,
    };

    // ── Part 1: content isolation ─────────────────────────────────────────────

    [Fact]
    public async Task PublicBanners_ReturnsOnly_RequestedBrandContent()
    {
        await using var db = NewDb();
        db.AppBanners.Add(Banner(BrandA, "home_top"));
        db.AppBanners.Add(Banner(BrandA, "home_top"));
        db.AppBanners.Add(Banner(BrandB, "home_top")); // other tenant — must never surface
        await db.SaveChangesAsync();

        var handler = new GetPublicBannersHandler(db);
        var result  = await handler.Handle(new GetPublicBannersQuery(BrandA, "home_top"), default);

        Assert.Equal(2, result.Count);
        Assert.All(result, dto => Assert.Equal(BrandA, dto.BrandId));
    }

    [Fact]
    public async Task PublicBanners_ForBrandWithNoContent_ReturnsEmpty_NotOtherTenant()
    {
        await using var db = NewDb();
        db.AppBanners.Add(Banner(BrandA, "home_top")); // only brand A has content
        await db.SaveChangesAsync();

        var handler = new GetPublicBannersHandler(db);
        // Brand B has no rows → must be empty, NOT brand A's banner (cross-tenant query returns nothing).
        var result  = await handler.Handle(new GetPublicBannersQuery(BrandB, null), default);

        Assert.Empty(result);
    }

    // ── Part 2: narrowed bypass allow-list ────────────────────────────────────

    // Mirrors Program.cs IsAllowlistedPublicCmsPath (file-local static; replicated to pin intent).
    private static bool IsAllowlistedPublicCmsPath(PathString path) =>
        path.StartsWithSegments("/api/v1/public/banners")
        || path.StartsWithSegments("/api/v1/public/onboarding-slides")
        || path.StartsWithSegments("/api/v1/public/app-config");

    [Theory]
    [InlineData("/api/v1/public/banners")]
    [InlineData("/api/v1/public/onboarding-slides")]
    [InlineData("/api/v1/public/app-config")]
    public void KnownPublicCmsRoutes_AreAllowlisted(string path)
        => Assert.True(IsAllowlistedPublicCmsPath(new PathString(path)));

    [Theory]
    [InlineData("/api/v1/public")]                  // bare prefix no longer auto-bypasses
    [InlineData("/api/v1/public/secrets")]          // hypothetical future route
    [InlineData("/api/v1/public/users")]
    [InlineData("/api/v1/publicx")]                 // segment-boundary guard
    public void OtherPublicPaths_AreNotAllowlisted(string path)
        => Assert.False(IsAllowlistedPublicCmsPath(new PathString(path)));
}
