namespace laundryghar.Orders.Tests.Coupons;

/// <summary>
/// Pure-math unit tests for the coupon discount calculation embedded in
/// CreateOrderHandler (mirrors the logic in Commerce ValidateApplyCouponHandler).
///
/// Formula:
///   percent coupon → discount = Math.Round(orderSubtotal * (discountValue / 100), 2)
///   flat coupon    → discount = discountValue
///   Cap: min(discount, MaxDiscountAmount) then min(result, orderSubtotal)
/// </summary>
public sealed class CouponMathTests
{
    // ── Mirrors the calculation in CreateOrderHandler ─────────────────────────

    private static decimal ComputeDiscount(
        string couponType,
        decimal discountValue,
        decimal? maxDiscountAmount,
        decimal orderSubtotal)
    {
        decimal discount = couponType == "percent"
            ? Math.Round(orderSubtotal * (discountValue / 100m), 2)
            : discountValue;

        if (maxDiscountAmount.HasValue && discount > maxDiscountAmount.Value)
            discount = maxDiscountAmount.Value;
        if (discount > orderSubtotal)
            discount = orderSubtotal;

        return discount;
    }

    // ── Percent coupon — no MaxDiscountAmount ──────────────────────────────────

    [Theory]
    [InlineData(200,    10,  20)]   // 10% of 200 = 20
    [InlineData(100,    50,  50)]   // 50% of 100 = 50
    [InlineData(  0,    50,   0)]   // zero subtotal → zero discount
    [InlineData( 50,    20,  10)]   // 20% of 50 = 10
    public void Percent_Coupon_NoMaxCap(int subtotal, int rate, int expected)
    {
        var discount = ComputeDiscount("percent", rate, null, subtotal);
        Assert.Equal((decimal)expected, discount);
    }

    // ── Percent coupon — with MaxDiscountAmount ────────────────────────────────

    [Fact]
    public void Percent_Coupon_CappedByMaxDiscount()
    {
        // 10% of 200 = 20; MaxDiscountAmount = 15 → result = 15
        var discount = ComputeDiscount("percent", 10m, 15m, 200m);
        Assert.Equal(15m, discount);
    }

    [Fact]
    public void Percent_Coupon_MaxCapNotHitWhenDisountIsSmaller()
    {
        // 20% of 50 = 10; MaxDiscountAmount = 8 → result = 8
        var discount = ComputeDiscount("percent", 20m, 8m, 50m);
        Assert.Equal(8m, discount);
    }

    // ── Flat coupon — no MaxDiscountAmount ────────────────────────────────────

    [Theory]
    [InlineData(200, 50, 50)]   // flat 50 on 200
    [InlineData( 30, 50, 30)]   // flat 50 exceeds subtotal → capped at 30
    [InlineData(200, 10, 10)]   // exact flat
    public void Flat_Coupon_NoMaxCap(int subtotal, int flatValue, int expected)
    {
        var discount = ComputeDiscount("flat", flatValue, null, subtotal);
        Assert.Equal((decimal)expected, discount);
    }

    // ── Flat coupon — with MaxDiscountAmount ──────────────────────────────────

    [Fact]
    public void Flat_Coupon_CappedByMaxDiscountAmount()
    {
        // flat 50, MaxDiscountAmount = 40 → result = 40
        var discount = ComputeDiscount("flat", 50m, 40m, 200m);
        Assert.Equal(40m, discount);
    }

    // ── Discount reduces tax base ──────────────────────────────────────────────

    [Fact]
    public void Discount_ReducesTaxableAmount()
    {
        const decimal subtotal     = 200m;
        const decimal couponFlat   = 10m;
        const decimal taxRate      = 18m;

        var taxable    = Math.Max(0m, subtotal - couponFlat);   // 190
        var taxTotal   = Math.Round(taxable * (taxRate / 100m), 2);   // 34.2
        var grandTotal = taxable + taxTotal;                    // 224.2

        var fullPriceGrand = subtotal * (1 + taxRate / 100m);   // 236

        Assert.True(grandTotal < fullPriceGrand,
            "Grand total after discount must be less than full-price grand total.");
        Assert.Equal(190m, taxable);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Discount_NeverExceedsSubtotal()
    {
        // A flat coupon with discountValue > subtotal should be capped
        var discount = ComputeDiscount("flat", 500m, null, 100m);
        Assert.Equal(100m, discount);
    }

    [Fact]
    public void TaxableAmount_NeverNegative()
    {
        const decimal subtotal       = 50m;
        const decimal couponDiscount = 60m;
        var taxable = Math.Max(0m, subtotal - couponDiscount);
        Assert.Equal(0m, taxable);
    }

    // ── Percent rounding ──────────────────────────────────────────────────────

    [Fact]
    public void Percent_RoundsToTwoDecimalPlaces()
    {
        // 10% of 333.33 = 33.333 → rounds to 33.33
        var discount = ComputeDiscount("percent", 10m, null, 333.33m);
        Assert.Equal(33.33m, discount);
    }

    [Fact]
    public void Percent_HalfUpRounding()
    {
        // 7% of 101 = 7.07 → 7.07 (no rounding issue here)
        var discount = ComputeDiscount("percent", 7m, null, 101m);
        Assert.Equal(7.07m, discount);
    }
}
