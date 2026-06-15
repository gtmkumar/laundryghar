using commerce.Application.Common.Interfaces;
using FluentValidation;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace commerce.Application.Commerce.Admin.Coupons;

// ── Queries ───────────────────────────────────────────────────────────────────

public sealed record GetCouponsQuery(int Page, int PageSize) : IQuery<PaginatedList<CouponDto>>;

public sealed class GetCouponsHandler : IQueryHandler<GetCouponsQuery, PaginatedList<CouponDto>>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetCouponsHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public Task<PaginatedList<CouponDto>> HandleAsync(GetCouponsQuery q, CancellationToken ct)
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

public sealed record GetCouponByIdQuery(Guid Id) : IQuery<CouponDto?>;

public sealed class GetCouponByIdHandler : IQueryHandler<GetCouponByIdQuery, CouponDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public GetCouponByIdHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CouponDto?> HandleAsync(GetCouponByIdQuery q, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.Coupons.FirstOrDefaultAsync(x => x.Id == q.Id && x.BrandId == brandId && x.DeletedAt == null, ct);
        return e is null ? null : GetCouponsHandler.ToDto(e);
    }
}

// ── Commands ──────────────────────────────────────────────────────────────────

public sealed record CreateCouponCommand(CreateCouponRequest Request, Guid? ActorId) : ICommand<CouponDto>;

public sealed class CreateCouponHandler : ICommandHandler<CreateCouponCommand, CouponDto>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public CreateCouponHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CouponDto> HandleAsync(CreateCouponCommand cmd, CancellationToken ct)
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

public sealed class CreateCouponValidator : AbstractValidator<CreateCouponRequest>
{
    public CreateCouponValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CouponType).NotEmpty();
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        RuleFor(x => x.CustomerEligibility).NotEmpty();
        RuleFor(x => x.MaxUsesPerCustomer).GreaterThan((short)0);
    }
}

public sealed record UpdateCouponCommand(Guid Id, UpdateCouponRequest Request, Guid? ActorId) : ICommand<CouponDto?>;

public sealed class UpdateCouponHandler : ICommandHandler<UpdateCouponCommand, CouponDto?>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateCouponHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<CouponDto?> HandleAsync(UpdateCouponCommand cmd, CancellationToken ct)
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

public sealed record DeleteCouponCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public sealed class DeleteCouponHandler : ICommandHandler<DeleteCouponCommand, bool>
{
    private readonly ICommerceDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteCouponHandler(ICommerceDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<bool> HandleAsync(DeleteCouponCommand cmd, CancellationToken ct)
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
