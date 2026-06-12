using FluentValidation;
using laundryghar.Orders.Application.Pickup.Dtos;
using laundryghar.SharedDataModel.Entities.Commerce;
using MediatR;

namespace laundryghar.Orders.Application.Pickup.Commands;

// ── Customer: validate coupon eligibility before submitting a pickup request ───

/// <summary>
/// Lightweight coupon preview for the checkout screen.
/// Does NOT write a redemption row — just checks eligibility and returns the
/// discount amount the customer would receive on their estimated cart total.
/// Mirrors the validation rules in Commerce ValidateApplyCouponHandler and
/// Orders CreateOrderCommand without committing any state.
/// </summary>
public sealed record ValidateCouponForPickupQuery(
    Guid CustomerId,
    Guid BrandId,
    string CouponCode,
    decimal EstimatedSubtotal
) : IRequest<CouponPreviewResult>;

/// <summary>Result returned by <see cref="ValidateCouponForPickupQuery"/>.</summary>
public sealed record CouponPreviewResult(
    bool Valid,
    /// <summary>Estimated discount in monetary units. 0 when the coupon is invalid.</summary>
    decimal DiscountPreview,
    /// <summary>Human-readable reason when <see cref="Valid"/> is false; null on success.</summary>
    string? Reason
);

public sealed class ValidateCouponForPickupHandler
    : IRequestHandler<ValidateCouponForPickupQuery, CouponPreviewResult>
{
    private readonly LaundryGharDbContext _db;

    public ValidateCouponForPickupHandler(LaundryGharDbContext db) => _db = db;

    public async Task<CouponPreviewResult> Handle(ValidateCouponForPickupQuery q, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var code = q.CouponCode.Trim().ToUpperInvariant();

        // 1. Existence + brand scope + status
        var coupon = await _db.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == code
                                   && x.BrandId == q.BrandId
                                   && x.DeletedAt == null, ct);

        if (coupon is null)
            return new(false, 0m, "Coupon not found.");

        if (coupon.Status != "active")
            return new(false, 0m, "Coupon is not active.");

        if (coupon.ValidFrom > now)
            return new(false, 0m, "Coupon is not yet valid.");

        if (coupon.ValidUntil.HasValue && coupon.ValidUntil < now)
            return new(false, 0m, "Coupon has expired.");

        if (coupon.MaxTotalUses.HasValue && coupon.CurrentUsageCount >= coupon.MaxTotalUses.Value)
            return new(false, 0m, "Coupon has reached its maximum global usage limit.");

        // 2. Minimum order value (against the customer's estimated subtotal)
        if (q.EstimatedSubtotal < coupon.MinOrderValue)
            return new(false, 0m, $"Order subtotal must be at least {coupon.MinOrderValue:F2} to use this coupon.");

        // 3. Per-customer usage
        var customerUsage = await _db.CouponRedemptions
            .CountAsync(r => r.CouponId == coupon.Id
                          && r.CustomerId == q.CustomerId
                          && r.RevertedAt == null, ct);

        if (coupon.IsSingleUsePerCust && customerUsage >= 1)
            return new(false, 0m, "This coupon can only be used once per customer.");

        if (customerUsage >= coupon.MaxUsesPerCustomer)
            return new(false, 0m, $"You have reached the maximum uses ({coupon.MaxUsesPerCustomer}) for this coupon.");

        // 4. Compute preview discount
        decimal discount = coupon.CouponType == "percent"
            ? Math.Round(q.EstimatedSubtotal * (coupon.DiscountValue / 100m), 2)
            : coupon.DiscountValue;

        if (coupon.MaxDiscountAmount.HasValue && discount > coupon.MaxDiscountAmount.Value)
            discount = coupon.MaxDiscountAmount.Value;
        if (discount > q.EstimatedSubtotal)
            discount = q.EstimatedSubtotal;

        return new(true, discount, null);
    }
}

public sealed class ValidateCouponForPickupValidator : AbstractValidator<ValidateCouponForPickupQuery>
{
    public ValidateCouponForPickupValidator()
    {
        RuleFor(x => x.CouponCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.EstimatedSubtotal).GreaterThanOrEqualTo(0);
    }
}

// ── Customer: reschedule a pending pickup request ─────────────────────────────

/// <summary>
/// Reschedules a pickup request the customer owns.
/// Allowed from: pending | no_response | rescheduled (mirrors the DB CHECK statuses
/// that represent "not yet collected" states).
/// Atomically:
///   1. Releases old slot booked_count (if any).
///   2. Increments new slot booked_count (if newSlotId provided), rejecting when full.
///   3. Updates the pickup request: newDate + newSlotId + status → rescheduled,
///      sets RescheduledFromId to the current request's id (self-referential lineage).
/// </summary>
public sealed record ReschedulePickupCommand(
    Guid PickupRequestId,
    Guid CustomerId,
    Guid BrandId,
    ReschedulePickupRequest Request,
    Guid? ActorId
) : IRequest<PickupRequestDto?>;

public sealed class ReschedulePickupHandler : IRequestHandler<ReschedulePickupCommand, PickupRequestDto?>
{
    /// <summary>Statuses from which rescheduling is permitted.</summary>
    private static readonly HashSet<string> ReschedulableStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "pending", "no_response", "rescheduled" };

    private readonly LaundryGharDbContext _db;
    private readonly ILogger<ReschedulePickupHandler> _logger;

    public ReschedulePickupHandler(LaundryGharDbContext db, ILogger<ReschedulePickupHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<PickupRequestDto?> Handle(ReschedulePickupCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        // Load pickup request and scope it to the caller's brand + customer (IDOR guard).
        var pr = await _db.PickupRequests
            .FirstOrDefaultAsync(p => p.Id     == cmd.PickupRequestId
                                   && p.CustomerId == cmd.CustomerId
                                   && p.BrandId   == cmd.BrandId, ct);

        if (pr is null) return null;

        if (!ReschedulableStatuses.Contains(pr.Status))
            throw new BusinessRuleException(
                $"Pickup request cannot be rescheduled from status '{pr.Status}'. " +
                "Only pending, no_response, or rescheduled requests can be rescheduled.");

        // Validate new slot when provided: must belong to this brand's store + have capacity.
        if (cmd.Request.NewSlotId.HasValue)
        {
            var slotExists = await _db.DeliverySlots
                .AnyAsync(s => s.Id       == cmd.Request.NewSlotId.Value
                            && s.BrandId  == cmd.BrandId
                            && s.IsActive, ct);
            if (!slotExists)
                throw new BusinessRuleException("The requested time slot does not exist or is not active.");
        }

        var now = DateTimeOffset.UtcNow;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // 1. Release old slot capacity (decrement booked_count, floor at 0).
            if (pr.PickupSlotId.HasValue)
            {
                await _db.Database.ExecuteSqlAsync(
                    $"""
                    UPDATE order_lifecycle.delivery_slots
                       SET booked_count = GREATEST(booked_count - 1, 0), updated_at = NOW()
                     WHERE id       = {pr.PickupSlotId.Value}
                       AND brand_id = {cmd.BrandId}
                    """, ct);

                // Cancel the existing slot booking record.
                var oldBooking = await _db.DeliverySlotBookings
                    .FirstOrDefaultAsync(b => b.PickupRequestId == pr.Id
                                           && b.SlotId          == pr.PickupSlotId.Value
                                           && b.Status          == "active", ct);
                if (oldBooking is not null)
                {
                    oldBooking.Status       = "cancelled";
                    oldBooking.CancelledAt  = now;
                    oldBooking.CancelledReason = "rescheduled";
                }
            }

            // 2. Book new slot (atomic capacity check — identical logic to CustomerSchedulePickupHandler).
            if (cmd.Request.NewSlotId.HasValue)
            {
                var updated = await _db.Database.ExecuteSqlAsync(
                    $"""
                    UPDATE order_lifecycle.delivery_slots
                       SET booked_count = booked_count + 1, updated_at = NOW()
                     WHERE id       = {cmd.Request.NewSlotId.Value}
                       AND brand_id = {cmd.BrandId}
                       AND booked_count < capacity
                       AND is_active   = true
                    """, ct);

                if (updated == 0)
                    throw new BusinessRuleException(
                        "The selected slot is full or unavailable. Please choose a different time slot.");

                // Create new slot booking record.
                var newSlot = await _db.DeliverySlots
                    .FirstOrDefaultAsync(s => s.Id == cmd.Request.NewSlotId.Value, ct);
                if (newSlot is not null)
                {
                    _db.DeliverySlotBookings.Add(new DeliverySlotBooking
                    {
                        Id              = Guid.NewGuid(),
                        SlotId          = cmd.Request.NewSlotId.Value,
                        BrandId         = cmd.BrandId,
                        StoreId         = newSlot.StoreId,
                        PickupRequestId = pr.Id,
                        CustomerId      = cmd.CustomerId,
                        BookingType     = "pickup",
                        BookedAt        = now,
                        Status          = "active",
                        CreatedAt       = now,
                        CreatedBy       = cmd.ActorId
                    });
                }
            }

            // 3. Update pickup request in-place.
            pr.PickupDate   = cmd.Request.NewDate;
            pr.PickupSlotId = cmd.Request.NewSlotId;
            pr.Status       = "rescheduled";
            pr.UpdatedAt    = now;
            pr.UpdatedBy    = cmd.ActorId;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "Pickup request {PickupRequestId} rescheduled to {NewDate} by customer {CustomerId}",
                pr.Id, cmd.Request.NewDate, cmd.CustomerId);
        });

        return CreatePickupRequestAdminHandler.ToDto(pr);
    }
}

public sealed class ReschedulePickupValidator : AbstractValidator<ReschedulePickupCommand>
{
    public ReschedulePickupValidator()
    {
        RuleFor(x => x.Request.NewDate)
            .Must(d => d >= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("New pickup date must be today or in the future.");
    }
}
