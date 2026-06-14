using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.AppBanners.Common;

internal static class AppBannerMapping
{
    /// <summary>Copies every client-supplied field onto <paramref name="entity"/>. Server-owned
    /// fields (id, brand, counters, audit, status) are set by the caller.</summary>
    public static AppBanner ApplyFields(this AppBanner entity, AppBannerFields f)
    {
        entity.AppType = f.AppType;
        entity.Placement = f.Placement;
        entity.Title = f.Title;
        entity.TitleLocalized = f.TitleLocalized;
        entity.Subtitle = f.Subtitle;
        entity.SubtitleLocalized = f.SubtitleLocalized;
        entity.ImageUrl = f.ImageUrl;
        entity.ImageDarkUrl = f.ImageDarkUrl;
        entity.CtaText = f.CtaText;
        entity.CtaDeeplink = f.CtaDeeplink;
        entity.ExternalUrl = f.ExternalUrl;
        entity.PromotionId = f.PromotionId;
        entity.CouponId = f.CouponId;
        entity.BackgroundColor = f.BackgroundColor;
        entity.DisplayOrder = f.DisplayOrder;
        entity.IsActive = f.IsActive;
        entity.ShowFrom = f.ShowFrom;
        entity.ShowUntil = f.ShowUntil;
        entity.TargetAudience = f.TargetAudience;
        entity.TargetSegments = f.TargetSegments;
        entity.TargetCities = f.TargetCities;
        entity.MinAppVersion = f.MinAppVersion;
        return entity;
    }

    /// <summary>Cross-brand IDOR guards: a banner may only reference a promotion/coupon owned
    /// by the same brand.</summary>
    public static async Task EnsureReferencesExistAsync(
        this ICoreDbContext db, Guid brandId, AppBannerFields f, CancellationToken ct)
    {
        if (f.PromotionId is { } promotionId &&
            !await db.Promotions.AnyAsync(p => p.Id == promotionId && p.BrandId == brandId, ct))
            throw new KeyNotFoundException("Promotion not found.");

        if (f.CouponId is { } couponId &&
            !await db.Coupons.AnyAsync(c => c.Id == couponId && c.BrandId == brandId && c.DeletedAt == null, ct))
            throw new KeyNotFoundException("Coupon not found.");
    }
}
