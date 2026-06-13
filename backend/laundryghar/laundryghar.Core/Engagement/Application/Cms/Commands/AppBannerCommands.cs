using FluentValidation;
using laundryghar.Engagement.Application.Cms.Dtos;
using MediatR;
using Microsoft.AspNetCore.Hosting;

namespace laundryghar.Engagement.Application.Cms.Commands;

// ── Create ─────────────────────────────────────────────────────────────────────

public sealed record CreateAppBannerCommand(
    CreateAppBannerRequest Request, Guid? ActorId) : IRequest<AppBannerDto>;

public sealed class CreateAppBannerHandler
    : IRequestHandler<CreateAppBannerCommand, AppBannerDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateAppBannerHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<AppBannerDto> Handle(CreateAppBannerCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Validate referenced promotion and coupon belong to this brand (cross-brand IDOR guards).
        if (req.PromotionId.HasValue)
        {
            var promotionInBrand = await _db.Promotions
                .AnyAsync(p => p.Id == req.PromotionId.Value && p.BrandId == brandId, ct);
            if (!promotionInBrand)
                throw new KeyNotFoundException("Promotion not found.");
        }

        if (req.CouponId.HasValue)
        {
            var couponInBrand = await _db.Coupons
                .AnyAsync(c => c.Id == req.CouponId.Value && c.BrandId == brandId && c.DeletedAt == null, ct);
            if (!couponInBrand)
                throw new KeyNotFoundException("Coupon not found.");
        }

        var entity = new AppBanner
        {
            Id               = Guid.NewGuid(),
            BrandId          = brandId,
            AppType          = req.AppType,
            Placement        = req.Placement,
            Title            = req.Title,
            TitleLocalized   = req.TitleLocalized,
            Subtitle         = req.Subtitle,
            SubtitleLocalized = req.SubtitleLocalized,
            ImageUrl         = req.ImageUrl,
            ImageDarkUrl     = req.ImageDarkUrl,
            CtaText          = req.CtaText,
            CtaDeeplink      = req.CtaDeeplink,
            ExternalUrl      = req.ExternalUrl,
            PromotionId      = req.PromotionId,
            CouponId         = req.CouponId,
            BackgroundColor  = req.BackgroundColor,
            DisplayOrder     = req.DisplayOrder,
            IsActive         = req.IsActive,
            ShowFrom         = req.ShowFrom,
            ShowUntil        = req.ShowUntil,
            TargetAudience   = req.TargetAudience,
            TargetSegments   = req.TargetSegments,
            TargetCities     = req.TargetCities,
            MinAppVersion    = req.MinAppVersion,
            ImpressionsCount = 0,
            ClicksCount      = 0,
            Status           = "active",
            CreatedAt        = now,
            UpdatedAt        = now,
            CreatedBy        = cmd.ActorId,
            UpdatedBy        = cmd.ActorId,
        };

        _db.AppBanners.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    internal static AppBannerDto ToDto(AppBanner e) => new(
        e.Id, e.BrandId, e.AppType, e.Placement,
        e.Title, e.TitleLocalized,
        e.Subtitle, e.SubtitleLocalized,
        e.ImageUrl, e.ImageDarkUrl,
        e.CtaText, e.CtaDeeplink, e.ExternalUrl,
        e.PromotionId, e.CouponId, e.BackgroundColor,
        e.DisplayOrder, e.IsActive,
        e.ShowFrom, e.ShowUntil,
        e.TargetAudience, e.TargetSegments, e.TargetCities,
        e.ImpressionsCount, e.ClicksCount, e.MinAppVersion,
        e.Status, e.CreatedAt, e.UpdatedAt);
}

// ── Update ─────────────────────────────────────────────────────────────────────

public sealed record UpdateAppBannerCommand(
    Guid Id, UpdateAppBannerRequest Request, Guid? ActorId) : IRequest<AppBannerDto?>;

public sealed class UpdateAppBannerHandler
    : IRequestHandler<UpdateAppBannerCommand, AppBannerDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateAppBannerHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<AppBannerDto?> Handle(UpdateAppBannerCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.AppBanners
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return null;

        var req = cmd.Request;

        // Validate referenced promotion and coupon belong to this brand (cross-brand IDOR guards).
        if (req.PromotionId.HasValue)
        {
            var promotionInBrand = await _db.Promotions
                .AnyAsync(p => p.Id == req.PromotionId.Value && p.BrandId == brandId, ct);
            if (!promotionInBrand)
                throw new KeyNotFoundException("Promotion not found.");
        }

        if (req.CouponId.HasValue)
        {
            var couponInBrand = await _db.Coupons
                .AnyAsync(c => c.Id == req.CouponId.Value && c.BrandId == brandId && c.DeletedAt == null, ct);
            if (!couponInBrand)
                throw new KeyNotFoundException("Coupon not found.");
        }
        entity.AppType           = req.AppType;
        entity.Placement         = req.Placement;
        entity.Title             = req.Title;
        entity.TitleLocalized    = req.TitleLocalized;
        entity.Subtitle          = req.Subtitle;
        entity.SubtitleLocalized = req.SubtitleLocalized;
        entity.ImageUrl          = req.ImageUrl;
        entity.ImageDarkUrl      = req.ImageDarkUrl;
        entity.CtaText           = req.CtaText;
        entity.CtaDeeplink       = req.CtaDeeplink;
        entity.ExternalUrl       = req.ExternalUrl;
        entity.PromotionId       = req.PromotionId;
        entity.CouponId          = req.CouponId;
        entity.BackgroundColor   = req.BackgroundColor;
        entity.DisplayOrder      = req.DisplayOrder;
        entity.IsActive          = req.IsActive;
        entity.ShowFrom          = req.ShowFrom;
        entity.ShowUntil         = req.ShowUntil;
        entity.TargetAudience    = req.TargetAudience;
        entity.TargetSegments    = req.TargetSegments;
        entity.TargetCities      = req.TargetCities;
        entity.MinAppVersion     = req.MinAppVersion;
        entity.Status            = req.Status;
        entity.UpdatedAt         = DateTimeOffset.UtcNow;
        entity.UpdatedBy         = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateAppBannerHandler.ToDto(entity);
    }
}

// ── Delete ─────────────────────────────────────────────────────────────────────

public sealed record DeleteAppBannerCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteAppBannerHandler
    : IRequestHandler<DeleteAppBannerCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteAppBannerHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> Handle(DeleteAppBannerCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.AppBanners
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return false;

        entity.Status    = "archived";
        entity.IsActive  = false;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Validators ─────────────────────────────────────────────────────────────────

/// <summary>Shared banner-field validation. Link targets (cta_deeplink/external_url/image_url)
/// are surfaced to the mobile clients and handed to router.push / Linking.openURL, so we
/// allowlist safe forms only and reject arbitrary URI schemes (intent://, file://, content://,
/// javascript:, …) at write time.
///
/// <para>
/// <paramref name="isDevelopment"/> controls whether plain <c>http://</c> URLs are accepted:
/// in non-Development environments only <c>https://</c> is allowed for HTTP URLs, preventing
/// mixed-content and MITM risks in production banner assets. Development is kept permissive
/// so local HTTP CDN / mock servers work without extra setup.
/// </para></summary>
internal static class AppBannerRules
{
    internal static readonly string[] ValidPlacements =
        ["home_top", "home_middle", "home_bottom", "services_top", "cart_top", "order_success", "profile"];

    // Allowed: empty/null, an in-app relative path ("/..."), an https URL, or the app's own
    // "laundryghar://" deep-link scheme. In Development, plain http:// is also accepted.
    // Anything else (intent://, file://, javascript:, ...) is rejected.
    internal static bool IsAllowedDeeplink(string? value, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.StartsWith('/')) return true;
        if (value.StartsWith("laundryghar://", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        // Allow plain http only in Development (e.g. local mock servers / tunnels).
        return isDevelopment && uri.Scheme == Uri.UriSchemeHttp;
    }

    // Allowed: empty/null, or an https absolute URL.
    // In Development, plain http:// is also accepted.
    internal static bool IsAllowedHttpUrl(string? value, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return isDevelopment && uri.Scheme == Uri.UriSchemeHttp;
    }
}

public sealed class CreateAppBannerValidator : AbstractValidator<CreateAppBannerCommand>
{
    public CreateAppBannerValidator(IWebHostEnvironment env)
    {
        var isDev = env.IsDevelopment();

        RuleFor(x => x.Request.Placement).NotEmpty()
            .Must(p => AppBannerRules.ValidPlacements.Contains(p))
            .WithMessage("placement must be one of: home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile");
        RuleFor(x => x.Request.TitleLocalized).NotEmpty();
        RuleFor(x => x.Request.SubtitleLocalized).NotEmpty();
        RuleFor(x => x.Request.ImageUrl).NotEmpty()
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "imageUrl must be an http(s) URL." : "imageUrl must be an https URL.");
        RuleFor(x => x.Request.ImageDarkUrl)
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "imageDarkUrl must be an http(s) URL." : "imageDarkUrl must be an https URL.");
        RuleFor(x => x.Request.CtaDeeplink)
            .Must(u => AppBannerRules.IsAllowedDeeplink(u, isDev))
            .WithMessage("ctaDeeplink must be a relative path, an https URL, or a laundryghar:// link.");
        RuleFor(x => x.Request.ExternalUrl)
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "externalUrl must be an http(s) URL." : "externalUrl must be an https URL.");
    }
}

public sealed class UpdateAppBannerValidator : AbstractValidator<UpdateAppBannerCommand>
{
    public UpdateAppBannerValidator(IWebHostEnvironment env)
    {
        var isDev = env.IsDevelopment();

        RuleFor(x => x.Request.Placement).NotEmpty()
            .Must(p => AppBannerRules.ValidPlacements.Contains(p))
            .WithMessage("placement must be one of: home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile");
        RuleFor(x => x.Request.TitleLocalized).NotEmpty();
        RuleFor(x => x.Request.SubtitleLocalized).NotEmpty();
        RuleFor(x => x.Request.ImageUrl).NotEmpty()
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "imageUrl must be an http(s) URL." : "imageUrl must be an https URL.");
        RuleFor(x => x.Request.ImageDarkUrl)
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "imageDarkUrl must be an http(s) URL." : "imageDarkUrl must be an https URL.");
        RuleFor(x => x.Request.CtaDeeplink)
            .Must(u => AppBannerRules.IsAllowedDeeplink(u, isDev))
            .WithMessage("ctaDeeplink must be a relative path, an https URL, or a laundryghar:// link.");
        RuleFor(x => x.Request.ExternalUrl)
            .Must(u => AppBannerRules.IsAllowedHttpUrl(u, isDev))
            .WithMessage(isDev ? "externalUrl must be an http(s) URL." : "externalUrl must be an https URL.");
    }
}
