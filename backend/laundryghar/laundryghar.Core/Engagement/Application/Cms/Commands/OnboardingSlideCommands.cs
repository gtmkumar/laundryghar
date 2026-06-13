using laundryghar.Engagement.Infrastructure.Services;
using ICurrentUser = laundryghar.Engagement.Infrastructure.Services.ICurrentUser;
using FluentValidation;
using laundryghar.Engagement.Application.Cms.Dtos;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Commands;

// ── Create ─────────────────────────────────────────────────────────────────────

public sealed record CreateOnboardingSlideCommand(
    CreateOnboardingSlideRequest Request, Guid? ActorId) : IRequest<OnboardingSlideDto>;

public sealed class CreateOnboardingSlideHandler
    : IRequestHandler<CreateOnboardingSlideCommand, OnboardingSlideDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateOnboardingSlideHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<OnboardingSlideDto> Handle(CreateOnboardingSlideCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new OnboardingSlide
        {
            Id                   = Guid.NewGuid(),
            BrandId              = brandId,
            AppType              = req.AppType,
            Title                = req.Title,
            TitleLocalized       = req.TitleLocalized,
            Description          = req.Description,
            DescriptionLocalized = req.DescriptionLocalized,
            ImageUrl             = req.ImageUrl,
            ImageDarkUrl         = req.ImageDarkUrl,
            AnimationUrl         = req.AnimationUrl,
            CtaText              = req.CtaText,
            CtaDeeplink          = req.CtaDeeplink,
            BackgroundColor      = req.BackgroundColor,
            TextColor            = req.TextColor,
            DisplayOrder         = req.DisplayOrder,
            IsActive             = req.IsActive,
            ShowFrom             = req.ShowFrom,
            ShowUntil            = req.ShowUntil,
            MinAppVersion        = req.MinAppVersion,
            MaxAppVersion        = req.MaxAppVersion,
            TargetSegments       = req.TargetSegments,
            Status               = "active",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.ActorId,
            UpdatedBy            = cmd.ActorId,
        };

        _db.OnboardingSlides.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    internal static OnboardingSlideDto ToDto(OnboardingSlide e) => new(
        e.Id, e.BrandId, e.AppType, e.Title, e.TitleLocalized,
        e.Description, e.DescriptionLocalized,
        e.ImageUrl, e.ImageDarkUrl, e.AnimationUrl,
        e.CtaText, e.CtaDeeplink,
        e.BackgroundColor, e.TextColor,
        e.DisplayOrder, e.IsActive,
        e.ShowFrom, e.ShowUntil,
        e.MinAppVersion, e.MaxAppVersion,
        e.TargetSegments, e.Status,
        e.CreatedAt, e.UpdatedAt);
}

// ── Update ─────────────────────────────────────────────────────────────────────

public sealed record UpdateOnboardingSlideCommand(
    Guid Id, UpdateOnboardingSlideRequest Request, Guid? ActorId) : IRequest<OnboardingSlideDto?>;

public sealed class UpdateOnboardingSlideHandler
    : IRequestHandler<UpdateOnboardingSlideCommand, OnboardingSlideDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateOnboardingSlideHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<OnboardingSlideDto?> Handle(UpdateOnboardingSlideCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.OnboardingSlides
            .FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.AppType              = req.AppType;
        entity.Title                = req.Title;
        entity.TitleLocalized       = req.TitleLocalized;
        entity.Description          = req.Description;
        entity.DescriptionLocalized = req.DescriptionLocalized;
        entity.ImageUrl             = req.ImageUrl;
        entity.ImageDarkUrl         = req.ImageDarkUrl;
        entity.AnimationUrl         = req.AnimationUrl;
        entity.CtaText              = req.CtaText;
        entity.CtaDeeplink          = req.CtaDeeplink;
        entity.BackgroundColor      = req.BackgroundColor;
        entity.TextColor            = req.TextColor;
        entity.DisplayOrder         = req.DisplayOrder;
        entity.IsActive             = req.IsActive;
        entity.ShowFrom             = req.ShowFrom;
        entity.ShowUntil            = req.ShowUntil;
        entity.MinAppVersion        = req.MinAppVersion;
        entity.MaxAppVersion        = req.MaxAppVersion;
        entity.TargetSegments       = req.TargetSegments;
        entity.Status               = req.Status;
        entity.UpdatedAt            = DateTimeOffset.UtcNow;
        entity.UpdatedBy            = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateOnboardingSlideHandler.ToDto(entity);
    }
}

// ── Delete ─────────────────────────────────────────────────────────────────────

public sealed record DeleteOnboardingSlideCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteOnboardingSlideHandler
    : IRequestHandler<DeleteOnboardingSlideCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteOnboardingSlideHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> Handle(DeleteOnboardingSlideCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.OnboardingSlides
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

public sealed class CreateOnboardingSlideValidator
    : AbstractValidator<CreateOnboardingSlideCommand>
{
    private static readonly string[] ValidAppTypes = ["customer", "rider", "staff", "pos"];

    public CreateOnboardingSlideValidator()
    {
        RuleFor(x => x.Request.AppType).NotEmpty()
            .Must(t => ValidAppTypes.Contains(t))
            .WithMessage("app_type must be one of: customer, rider, staff, pos");
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.TitleLocalized).NotEmpty();
        RuleFor(x => x.Request.DescriptionLocalized).NotEmpty();
        RuleFor(x => x.Request.ImageUrl).NotEmpty();
    }
}
