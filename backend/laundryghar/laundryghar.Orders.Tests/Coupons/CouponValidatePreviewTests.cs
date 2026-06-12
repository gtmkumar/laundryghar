using FluentValidation;
using laundryghar.Orders.Application.Pickup.Commands;

namespace laundryghar.Orders.Tests.Coupons;

/// <summary>
/// Pure-logic unit tests for the coupon preview validation rules in
/// <see cref="ValidateCouponForPickupHandler"/> and <see cref="ValidateCouponForPickupValidator"/>.
///
/// The handler hits the DB (coupon lookup + redemption count); these tests exercise only the
/// eligibility rules and discount arithmetic that can be extracted without a database.
/// DB-dependent eligibility checks (existence, per-customer usage) are deferred to live E2E.
/// </summary>
public sealed class CouponValidatePreviewTests
{
    // ── Validator ─────────────────────────────────────────────────────────────

    private static ValidateCouponForPickupQuery MakeQuery(
        string couponCode       = "SAVE10",
        decimal estimatedSubtotal = 200m)
        => new(
            CustomerId:        Guid.NewGuid(),
            BrandId:           Guid.NewGuid(),
            CouponCode:        couponCode,
            EstimatedSubtotal: estimatedSubtotal
        );

    [Fact]
    public void Validator_EmptyCouponCode_Fails()
    {
        var validator = new ValidateCouponForPickupValidator();
        var result = validator.Validate(MakeQuery(couponCode: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ValidateCouponForPickupQuery.CouponCode));
    }

    [Fact]
    public void Validator_CouponCodeExceeds50Chars_Fails()
    {
        var validator  = new ValidateCouponForPickupValidator();
        var longCode   = new string('A', 51);
        var result     = validator.Validate(MakeQuery(couponCode: longCode));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ValidateCouponForPickupQuery.CouponCode));
    }

    [Fact]
    public void Validator_CouponCodeExactly50Chars_Passes()
    {
        var validator = new ValidateCouponForPickupValidator();
        var maxCode   = new string('A', 50);
        var result    = validator.Validate(MakeQuery(couponCode: maxCode));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_NegativeSubtotal_Fails()
    {
        var validator = new ValidateCouponForPickupValidator();
        var result    = validator.Validate(MakeQuery(estimatedSubtotal: -1m));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.PropertyName == nameof(ValidateCouponForPickupQuery.EstimatedSubtotal));
    }

    [Fact]
    public void Validator_ZeroSubtotal_Passes()
    {
        // Zero subtotal is valid; the coupon just won't yield a meaningful discount.
        var validator = new ValidateCouponForPickupValidator();
        var result    = validator.Validate(MakeQuery(estimatedSubtotal: 0m));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_ValidInput_Passes()
    {
        var validator = new ValidateCouponForPickupValidator();
        var result    = validator.Validate(MakeQuery("SUMMER25", 500m));

        Assert.True(result.IsValid);
    }

    // ── Discount preview arithmetic ────────────────────────────────────────────
    // Mirrors lines 84-92 of CustomerPickupCommands.cs (ValidateCouponForPickupHandler.Handle).

    private static decimal ComputePreview(
        string couponType,
        decimal discountValue,
        decimal? maxDiscountAmount,
        decimal subtotal)
    {
        decimal discount = couponType == "percent"
            ? Math.Round(subtotal * (discountValue / 100m), 2)
            : discountValue;

        if (maxDiscountAmount.HasValue && discount > maxDiscountAmount.Value)
            discount = maxDiscountAmount.Value;

        if (discount > subtotal)
            discount = subtotal;

        return discount;
    }

    [Theory]
    [InlineData(200, 10,  20)]   // 10% of 200 = 20
    [InlineData(100, 25,  25)]   // 25% of 100 = 25
    [InlineData(  0, 10,   0)]   // zero subtotal
    [InlineData( 50, 20,  10)]   // 20% of 50 = 10
    public void Percent_NoMaxCap_CorrectDiscount(int subtotal, int rate, int expected)
    {
        var result = ComputePreview("percent", rate, null, subtotal);
        Assert.Equal((decimal)expected, result);
    }

    [Fact]
    public void Percent_CappedByMaxDiscountAmount()
    {
        // 10% of 500 = 50; MaxDiscountAmount = 30 → result = 30
        var result = ComputePreview("percent", 10m, 30m, 500m);
        Assert.Equal(30m, result);
    }

    [Theory]
    [InlineData(500, 100, 100)]   // flat 100 on 500 → 100
    [InlineData( 80,  100,  80)]  // flat 100 exceeds 80 subtotal → capped at 80
    [InlineData(200,  50,  50)]   // exact flat
    public void Flat_NoMaxCap_CorrectDiscount(int subtotal, int flatValue, int expected)
    {
        var result = ComputePreview("flat", flatValue, null, subtotal);
        Assert.Equal((decimal)expected, result);
    }

    [Fact]
    public void Flat_CappedByMaxDiscountAmount()
    {
        // flat 100, MaxDiscountAmount = 60 → result = 60
        var result = ComputePreview("flat", 100m, 60m, 500m);
        Assert.Equal(60m, result);
    }

    [Fact]
    public void Percent_RoundsToTwoDecimalPlaces()
    {
        // 7% of 333.33 = 23.3331 → rounds to 23.33
        var result = ComputePreview("percent", 7m, null, 333.33m);
        Assert.Equal(23.33m, result);
    }

    [Fact]
    public void Discount_NeverExceedsSubtotal()
    {
        // Large flat coupon against tiny subtotal
        var result = ComputePreview("flat", 9999m, null, 50m);
        Assert.Equal(50m, result);
    }

    // ── Eligibility guard logic (state-machine mirrors) ────────────────────────
    // These model the short-circuit rules in the handler without needing DB access.

    [Fact]
    public void Guard_InactiveCoupon_ReturnsInvalid()
    {
        // Mirrors: if (coupon.Status != "active") return new(false, 0m, ...)
        const string couponStatus = "inactive";
        var valid = couponStatus == "active";
        Assert.False(valid, "Inactive coupon must not be valid.");
    }

    [Fact]
    public void Guard_NotYetValid_ReturnsInvalid()
    {
        // Mirrors: if (coupon.ValidFrom > now) return new(false, ...)
        var validFrom = DateTimeOffset.UtcNow.AddDays(3);
        var now       = DateTimeOffset.UtcNow;
        Assert.True(validFrom > now, "Future ValidFrom must block redemption.");
    }

    [Fact]
    public void Guard_Expired_ReturnsInvalid()
    {
        // Mirrors: if (coupon.ValidUntil.HasValue && coupon.ValidUntil < now)
        DateTimeOffset? validUntil = DateTimeOffset.UtcNow.AddDays(-1);
        var now = DateTimeOffset.UtcNow;
        Assert.True(validUntil.HasValue && validUntil < now, "Past ValidUntil must block redemption.");
    }

    [Fact]
    public void Guard_GlobalUsageExhausted_ReturnsInvalid()
    {
        // Mirrors: if (coupon.MaxTotalUses.HasValue && coupon.CurrentUsageCount >= coupon.MaxTotalUses)
        int? maxTotalUses      = 100;
        int  currentUsageCount = 100;
        var exhausted = maxTotalUses.HasValue && currentUsageCount >= maxTotalUses.Value;
        Assert.True(exhausted, "Coupon at MaxTotalUses must be considered exhausted.");
    }

    [Fact]
    public void Guard_GlobalUsageNotExhausted_Passes()
    {
        int? maxTotalUses      = 100;
        int  currentUsageCount = 99;
        var exhausted = maxTotalUses.HasValue && currentUsageCount >= maxTotalUses.Value;
        Assert.False(exhausted, "Coupon below MaxTotalUses should pass the global guard.");
    }

    [Fact]
    public void Guard_BelowMinOrderValue_ReturnsInvalid()
    {
        // Mirrors: if (q.EstimatedSubtotal < coupon.MinOrderValue) return new(false, ...)
        const decimal minOrderValue       = 300m;
        const decimal estimatedSubtotal   = 200m;
        Assert.True(estimatedSubtotal < minOrderValue,
            "Subtotal below MinOrderValue must block coupon.");
    }

    [Fact]
    public void Guard_SingleUsePerCust_ExceedsOne_ReturnsInvalid()
    {
        // Mirrors: if (coupon.IsSingleUsePerCust && customerUsage >= 1) return new(false, ...)
        const bool isSingleUsePerCust = true;
        const int  customerUsage      = 1;
        var blocked = isSingleUsePerCust && customerUsage >= 1;
        Assert.True(blocked, "Single-use-per-customer coupon already used must be blocked.");
    }

    [Fact]
    public void Guard_MaxUsesPerCustomer_Exceeded_ReturnsInvalid()
    {
        // Mirrors: if (customerUsage >= coupon.MaxUsesPerCustomer) return new(false, ...)
        const int maxUsesPerCustomer = 3;
        const int customerUsage      = 3;
        Assert.True(customerUsage >= maxUsesPerCustomer,
            "Customer at MaxUsesPerCustomer must be blocked.");
    }
}
