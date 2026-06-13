using FluentValidation;
using laundryghar.Commerce.Application;
using laundryghar.Utilities.Common;
using MediatR;

using laundryghar.Commerce.Infrastructure.Services;
namespace laundryghar.Commerce.Application.Admin.Coupons;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetCouponsQuery(int Page, int PageSize) : IRequest<PaginatedList<CouponDto>>;

public sealed class GetCouponsHandler : IRequestHandler<GetCouponsQuery, PaginatedList<CouponDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetCouponsHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<CouponDto>> Handle(GetCouponsQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var query = _db.Coupons
            .Where(x => x.BrandId == brandId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToDto(x));
        return PaginatedList<CouponDto>.CreateAsync(query, q.Page, q.PageSize, ct);
    }

    internal static CouponDto ToDto(Coupon x) => new(
        x.Id, x.BrandId, x.Code, x.Name, x.Description, x.CouponType,
        x.DiscountValue, x.MaxDiscountAmount, x.MinOrderValue,
        x.ApplicableServices, x.ApplicableStores, x.ApplicableFranchises,
        x.CustomerEligibility, x.IsFirstOrderOnly, x.IsSingleUsePerCust,
        x.MaxTotalUses, x.MaxUsesPerCustomer, x.CurrentUsageCount,
        x.IsStackable, x.IsPublic, x.IsAutoApply,
        x.ValidFrom, x.ValidUntil, x.Status, x.CreatedAt, x.UpdatedAt);
}

public sealed record GetCouponByIdQuery(Guid Id) : IRequest<CouponDto?>;

public sealed class GetCouponByIdHandler : IRequestHandler<GetCouponByIdQuery, CouponDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetCouponByIdHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CouponDto?> Handle(GetCouponByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Coupons.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        return e is null ? null : GetCouponsHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreateCouponCommand(CreateCouponRequest Request, Guid? ActorId) : IRequest<CouponDto>;

public sealed class CreateCouponHandler : IRequestHandler<CreateCouponCommand, CouponDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateCouponHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CouponDto> Handle(CreateCouponCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        var entity = new Coupon
        {
            Id                   = Guid.NewGuid(),
            BrandId              = brandId,
            Code                 = req.Code.ToUpperInvariant(),
            Name                 = req.Name,
            Description          = req.Description,
            CouponType           = req.CouponType,
            DiscountValue        = req.DiscountValue,
            MaxDiscountAmount    = req.MaxDiscountAmount,
            MinOrderValue        = req.MinOrderValue,
            ApplicableServices   = req.ApplicableServices ?? [],
            ApplicableStores     = req.ApplicableStores ?? [],
            ApplicableFranchises = req.ApplicableFranchises ?? [],
            CustomerEligibility  = req.CustomerEligibility,
            EligibleCustomerIds  = req.EligibleCustomerIds,
            EligibleSegments     = req.EligibleSegments,
            IsFirstOrderOnly     = req.IsFirstOrderOnly,
            IsSingleUsePerCust   = req.IsSingleUsePerCust,
            MaxTotalUses         = req.MaxTotalUses,
            MaxUsesPerCustomer   = req.MaxUsesPerCustomer,
            CurrentUsageCount    = 0,
            IsStackable          = req.IsStackable,
            IsPublic             = req.IsPublic,
            IsAutoApply          = req.IsAutoApply,
            ValidFrom            = req.ValidFrom,
            ValidUntil           = req.ValidUntil,
            Status               = "active",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.ActorId,
            UpdatedBy            = cmd.ActorId
        };

        _db.Coupons.Add(entity);
        await _db.SaveChangesAsync(ct);
        return GetCouponsHandler.ToDto(entity);
    }
}

public sealed class CreateCouponValidator : AbstractValidator<CreateCouponCommand>
{
    public CreateCouponValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.CouponType).NotEmpty();
        RuleFor(x => x.Request.DiscountValue).GreaterThan(0);
        RuleFor(x => x.Request.CustomerEligibility).NotEmpty();
        RuleFor(x => x.Request.MaxUsesPerCustomer).GreaterThan((short)0);
    }
}

public sealed record UpdateCouponCommand(Guid Id, UpdateCouponRequest Request, Guid? ActorId) : IRequest<CouponDto?>;

public sealed class UpdateCouponHandler : IRequestHandler<UpdateCouponCommand, CouponDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateCouponHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CouponDto?> Handle(UpdateCouponCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.Coupons.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        if (entity is null) return null;

        var req = cmd.Request;
        entity.Name                = req.Name;
        entity.Description         = req.Description;
        entity.DiscountValue       = req.DiscountValue;
        entity.MaxDiscountAmount   = req.MaxDiscountAmount;
        entity.MinOrderValue       = req.MinOrderValue;
        entity.ApplicableServices  = req.ApplicableServices ?? [];
        entity.ApplicableStores    = req.ApplicableStores ?? [];
        entity.ApplicableFranchises = req.ApplicableFranchises ?? [];
        entity.CustomerEligibility = req.CustomerEligibility;
        entity.EligibleCustomerIds = req.EligibleCustomerIds;
        entity.EligibleSegments    = req.EligibleSegments;
        entity.IsFirstOrderOnly    = req.IsFirstOrderOnly;
        entity.IsSingleUsePerCust  = req.IsSingleUsePerCust;
        entity.MaxTotalUses        = req.MaxTotalUses;
        entity.MaxUsesPerCustomer  = req.MaxUsesPerCustomer;
        entity.IsStackable         = req.IsStackable;
        entity.IsPublic            = req.IsPublic;
        entity.IsAutoApply         = req.IsAutoApply;
        entity.ValidFrom           = req.ValidFrom;
        entity.ValidUntil          = req.ValidUntil;
        entity.Status              = req.Status;
        entity.UpdatedAt           = DateTimeOffset.UtcNow;
        entity.UpdatedBy           = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return GetCouponsHandler.ToDto(entity);
    }
}

public sealed record DeleteCouponCommand(Guid Id, Guid? ActorId) : IRequest<bool>;

public sealed class DeleteCouponHandler : IRequestHandler<DeleteCouponCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteCouponHandler(LaundryGharDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> Handle(DeleteCouponCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.Coupons.FirstOrDefaultAsync(x => x.Id == cmd.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        if (entity is null) return false;

        // Soft-delete must also move status off 'active' so status-keyed reports don't
        // miscount archived coupons. The coupons CHECK constraint has no 'archived' value;
        // 'retired' is its terminal/archived state.
        entity.Status    = "retired";
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = cmd.ActorId;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
