using FluentValidation;
using laundryghar.Engagement.Application.Cms.Dtos;
using MediatR;

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

public sealed class CreateAppBannerValidator : AbstractValidator<CreateAppBannerCommand>
{
    private static readonly string[] ValidPlacements =
        ["home_top", "home_middle", "home_bottom", "services_top", "cart_top", "order_success", "profile"];

    public CreateAppBannerValidator()
    {
        RuleFor(x => x.Request.Placement).NotEmpty()
            .Must(p => ValidPlacements.Contains(p))
            .WithMessage("placement must be one of: home_top, home_middle, home_bottom, services_top, cart_top, order_success, profile");
        RuleFor(x => x.Request.TitleLocalized).NotEmpty();
        RuleFor(x => x.Request.SubtitleLocalized).NotEmpty();
        RuleFor(x => x.Request.ImageUrl).NotEmpty();
    }
}
