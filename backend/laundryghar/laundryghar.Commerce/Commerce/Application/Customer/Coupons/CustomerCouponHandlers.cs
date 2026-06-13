using FluentValidation;
using laundryghar.Commerce.Application;
using MediatR;

namespace laundryghar.Commerce.Application.Customer.Coupons;

// ── Get applicable coupons ────────────────────────────────────────────────────

public sealed record GetApplicableCouponsQuery(Guid CustomerId, Guid BrandId) : IRequest<List<CouponDto>>;

public sealed class GetApplicableCouponsHandler : IRequestHandler<GetApplicableCouponsQuery, List<CouponDto>>
{
    private readonly LaundryGharDbContext _db;

    public GetApplicableCouponsHandler(LaundryGharDbContext db) => _db = db;

    public async Task<List<CouponDto>> Handle(GetApplicableCouponsQuery q, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await _db.Coupons
            .Where(x => x.BrandId == q.BrandId
                     && x.DeletedAt == null
                     && x.Status == "active"
                     && x.IsPublic
                     && x.ValidFrom <= now
                     && (x.ValidUntil == null || x.ValidUntil >= now)
                     && (x.MaxTotalUses == null || x.CurrentUsageCount < x.MaxTotalUses))
            .Select(x => new CouponDto(
                x.Id, x.BrandId, x.Code, x.Name, x.Description, x.CouponType,
                x.DiscountValue, x.MaxDiscountAmount, x.MinOrderValue,
                x.ApplicableServices, x.ApplicableStores, x.ApplicableFranchises,
                x.CustomerEligibility, x.IsFirstOrderOnly, x.IsSingleUsePerCust,
                x.MaxTotalUses, x.MaxUsesPerCustomer, x.CurrentUsageCount,
                x.IsStackable, x.IsPublic, x.IsAutoApply,
                x.ValidFrom, x.ValidUntil, x.Status, x.CreatedAt, x.UpdatedAt))
            .ToListAsync(ct);
    }
}

// ── Validate and apply coupon ─────────────────────────────────────────────────

/// <summary>
/// Eligibility checks (in order):
///  1. Coupon exists, belongs to brand, is active, not expired, and not globally exhausted.
///  2. MinOrderValue check.
///  3. Per-customer usage limit (MaxUsesPerCustomer, IsSingleUsePerCust).
///  4. IsFirstOrderOnly: customer must have zero completed orders (placeholder: check redemptions).
///  5. One coupon per order (coupon_redemptions unique on order_id).
/// On success: inserts coupon_redemptions row + increments coupon.current_usage_count atomically.
/// </summary>
public sealed record ValidateApplyCouponCommand(
    Guid CustomerId,
    Guid BrandId,
    ValidateCouponRequest Request
) : IRequest<CouponRedemptionDto>;

public sealed class ValidateApplyCouponHandler : IRequestHandler<ValidateApplyCouponCommand, CouponRedemptionDto>
{
    private readonly LaundryGharDbContext _db;

    public ValidateApplyCouponHandler(LaundryGharDbContext db) => _db = db;

    public async Task<CouponRedemptionDto> Handle(ValidateApplyCouponCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // 1. Load and validate coupon
        var coupon = await _db.Coupons
            .FirstOrDefaultAsync(x => x.Code == req.CouponCode.ToUpperInvariant()
                                   && x.BrandId == cmd.BrandId
                                   && x.DeletedAt == null, ct);

        if (coupon is null)
            throw new BusinessRuleException("Coupon not found.");
        if (coupon.Status != "active")
            throw new BusinessRuleException("Coupon is not active.");
        if (coupon.ValidFrom > now)
            throw new BusinessRuleException("Coupon is not yet valid.");
        if (coupon.ValidUntil.HasValue && coupon.ValidUntil < now)
            throw new BusinessRuleException("Coupon has expired.");
        if (coupon.MaxTotalUses.HasValue && coupon.CurrentUsageCount >= coupon.MaxTotalUses.Value)
            throw new BusinessRuleException("Coupon has reached its maximum global usage limit.");

        // 2. Minimum order value
        if (req.OrderSubtotal < coupon.MinOrderValue)
            throw new BusinessRuleException($"Order subtotal must be at least {coupon.MinOrderValue} to use this coupon.");

        // 3. Per-customer usage check
        var customerUsageCount = await _db.CouponRedemptions
            .CountAsync(r => r.CouponId == coupon.Id
                          && r.CustomerId == cmd.CustomerId
                          && r.RevertedAt == null, ct);

        if (coupon.IsSingleUsePerCust && customerUsageCount >= 1)
            throw new BusinessRuleException("This coupon can only be used once per customer.");
        if (customerUsageCount >= coupon.MaxUsesPerCustomer)
            throw new BusinessRuleException($"You have reached the maximum uses ({coupon.MaxUsesPerCustomer}) for this coupon.");

        // 4. One coupon per order
        var alreadyOnOrder = await _db.CouponRedemptions
            .AnyAsync(r => r.OrderId == req.OrderId && r.RevertedAt == null, ct);
        if (alreadyOnOrder)
            throw new BusinessRuleException("A coupon has already been applied to this order.");

        // 5. Calculate discount
        decimal discount = coupon.CouponType == "percent"
            ? req.OrderSubtotal * (coupon.DiscountValue / 100m)
            : coupon.DiscountValue;

        if (coupon.MaxDiscountAmount.HasValue && discount > coupon.MaxDiscountAmount.Value)
            discount = coupon.MaxDiscountAmount.Value;
        if (discount > req.OrderSubtotal)
            discount = req.OrderSubtotal;

        // 6. Record redemption + increment usage count atomically
        CouponRedemption redemption = null!;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var txn = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                redemption = new CouponRedemption
                {
                    Id                       = Guid.NewGuid(),
                    CouponId                 = coupon.Id,
                    BrandId                  = cmd.BrandId,
                    CustomerId               = cmd.CustomerId,
                    OrderId                  = req.OrderId,
                    OrderCreatedAt           = req.OrderCreatedAt,
                    CouponCode               = coupon.Code,
                    DiscountAmount           = discount,
                    OrderSubtotalSnapshot    = req.OrderSubtotal,
                    RedeemedAt               = now,
                    Metadata                 = "{}",
                    CreatedAt                = now,
                    CreatedBy                = cmd.CustomerId
                };
                _db.CouponRedemptions.Add(redemption);

                coupon.CurrentUsageCount++;
                coupon.UpdatedAt = now;

                await _db.SaveChangesAsync(ct);
                await txn.CommitAsync(ct);
            }
            catch
            {
                await txn.RollbackAsync(ct);
                throw;
            }
        });

        return new CouponRedemptionDto(
            redemption.Id, redemption.CouponId, redemption.CouponCode,
            redemption.CustomerId, redemption.OrderId, redemption.OrderCreatedAt,
            redemption.DiscountAmount, redemption.OrderSubtotalSnapshot,
            redemption.RedeemedAt, redemption.RevertedAt, redemption.CreatedAt);
    }
}

public sealed class ValidateApplyCouponValidator : AbstractValidator<ValidateApplyCouponCommand>
{
    public ValidateApplyCouponValidator()
    {
        RuleFor(x => x.Request.CouponCode).NotEmpty();
        RuleFor(x => x.Request.OrderId).NotEmpty();
        RuleFor(x => x.Request.OrderSubtotal).GreaterThanOrEqualTo(0);
    }
}
