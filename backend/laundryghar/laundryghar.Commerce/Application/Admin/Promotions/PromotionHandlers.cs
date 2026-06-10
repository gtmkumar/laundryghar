using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Commerce.Application.Admin.Promotions;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetPromotionsQuery(int Page, int PageSize) : IRequest<PaginatedList<PromotionDto>>;

public sealed class GetPromotionsHandler : IRequestHandler<GetPromotionsQuery, PaginatedList<PromotionDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPromotionsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<PromotionDto>> Handle(GetPromotionsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.Promotions
            .Where(x => x.BrandId == brandId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToDto(x));
        return PaginatedList<PromotionDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }

    internal static PromotionDto ToDto(Promotion x) => new(
        x.Id, x.BrandId, x.Code, x.Name, x.Description, x.PromotionType, x.TargetAudience,
        x.EligibleSegments, x.Rules, x.RewardConfig, x.CouponId, x.BannerImageUrl, x.DeeplinkUrl,
        x.ValidFrom, x.ValidUntil, x.TotalBudget, x.SpentBudget, x.RedemptionsCount,
        x.Status, x.CreatedAt, x.UpdatedAt);
}

public sealed record GetPromotionByIdQuery(Guid Id) : IRequest<PromotionDto?>;

public sealed class GetPromotionByIdHandler : IRequestHandler<GetPromotionByIdQuery, PromotionDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetPromotionByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PromotionDto?> Handle(GetPromotionByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Promotions.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId, ct);
        return e is null ? null : GetPromotionsHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreatePromotionCommand(CreatePromotionRequest Request, Guid? ActorId) : IRequest<PromotionDto>;

public sealed class CreatePromotionHandler : IRequestHandler<CreatePromotionCommand, PromotionDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreatePromotionHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PromotionDto> Handle(CreatePromotionCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // Validate the linked coupon belongs to this brand, when supplied (cross-brand IDOR guard).
        if (req.CouponId.HasValue)
        {
            var couponInBrand = await _db.Coupons
                .AnyAsync(c => c.Id == req.CouponId.Value && c.BrandId == brandId && c.DeletedAt == null, ct);
            if (!couponInBrand)
                throw new KeyNotFoundException("Coupon not found.");
        }

        var entity = new Promotion
        {
            Id               = Guid.NewGuid(),
            BrandId          = brandId,
            Code             = req.Code,
            Name             = req.Name,
            Description      = req.Description,
            PromotionType    = req.PromotionType,
            TargetAudience   = req.TargetAudience,
            EligibleSegments = req.EligibleSegments,
            Rules            = req.Rules,
            RewardConfig     = req.RewardConfig,
            CouponId         = req.CouponId,
            BannerImageUrl   = req.BannerImageUrl,
            DeeplinkUrl      = req.DeeplinkUrl,
            ValidFrom        = req.ValidFrom,
            ValidUntil       = req.ValidUntil,
            TotalBudget      = req.TotalBudget,
            SpentBudget      = 0m,
            ImpressionsCount = 0,
            RedemptionsCount = 0,
            Status           = "active",
            CreatedAt        = now,
            UpdatedAt        = now,
            CreatedBy        = cmd.ActorId,
            UpdatedBy        = cmd.ActorId
        };

        _db.Promotions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return GetPromotionsHandler.ToDto(entity);
    }
}

public sealed class CreatePromotionValidator : AbstractValidator<CreatePromotionCommand>
{
    public CreatePromotionValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.PromotionType).NotEmpty();
        RuleFor(x => x.Request.TargetAudience).NotEmpty();
        RuleFor(x => x.Request.Rules).NotEmpty();
        RuleFor(x => x.Request.RewardConfig).NotEmpty();
    }
}

public sealed record UpdatePromotionCommand(Guid Id, UpdatePromotionRequest Request, Guid? ActorId) : IRequest<PromotionDto?>;

public sealed class UpdatePromotionHandler : IRequestHandler<UpdatePromotionCommand, PromotionDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdatePromotionHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<PromotionDto?> Handle(UpdatePromotionCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.Promotions.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return null;

        var req = cmd.Request;

        // Validate the linked coupon belongs to this brand, when supplied (cross-brand IDOR guard).
        if (req.CouponId.HasValue)
        {
            var couponInBrand = await _db.Coupons
                .AnyAsync(c => c.Id == req.CouponId.Value && c.BrandId == brandId && c.DeletedAt == null, ct);
            if (!couponInBrand)
                throw new KeyNotFoundException("Coupon not found.");
        }
        entity.Name             = req.Name;
        entity.Description      = req.Description;
        entity.TargetAudience   = req.TargetAudience;
        entity.EligibleSegments = req.EligibleSegments;
        entity.Rules            = req.Rules;
        entity.RewardConfig     = req.RewardConfig;
        entity.CouponId         = req.CouponId;
        entity.BannerImageUrl   = req.BannerImageUrl;
        entity.DeeplinkUrl      = req.DeeplinkUrl;
        entity.ValidFrom        = req.ValidFrom;
        entity.ValidUntil       = req.ValidUntil;
        entity.TotalBudget      = req.TotalBudget;
        entity.Status           = req.Status;
        entity.UpdatedAt        = DateTimeOffset.UtcNow;
        entity.UpdatedBy        = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return GetPromotionsHandler.ToDto(entity);
    }
}

public sealed record DeletePromotionCommand(Guid Id) : IRequest<bool>;

public sealed class DeletePromotionHandler : IRequestHandler<DeletePromotionCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeletePromotionHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeletePromotionCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.Promotions.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId, ct);
        if (entity is null) return false;

        _db.Promotions.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
