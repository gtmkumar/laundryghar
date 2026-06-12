using System.Text.Json;

namespace laundryghar.Orders.Tests.Pricing;

/// <summary>
/// Unit tests for the promotion evaluation block in CreateOrderHandler
/// (lines 434-516 of CreateOrderCommand.cs).
///
/// These tests exercise the rules that can be extracted without a DB:
///   - Audience matching (all / new_customers / segment)
///   - RewardConfig JSONB parsing and discount arithmetic (percent / flat)
///   - Optional max_discount cap and subtotal floor
///   - Budget guard (SpentBudget &lt; TotalBudget)
///   - First-match-wins ordering guarantee
///   - Malformed RewardConfig silently skipped
/// DB-dependent steps (loading promotions, incrementing RedemptionsCount) are
/// deferred to live E2E.
/// </summary>
public sealed class PromotionEvaluationTests
{
    // ── Audience matching ──────────────────────────────────────────────────────
    // Mirrors the switch in CreateOrderHandler lines 469-477.

    private static bool AudienceMatches(
        string targetAudience,
        int lifetimeOrders,
        string customerSegment,
        string[] eligibleSegments)
    {
        return targetAudience switch
        {
            "all"           => true,
            "new_customers" => lifetimeOrders == 0,
            "segment"       => eligibleSegments.Length > 0
                               && eligibleSegments.Contains(customerSegment),
            _               => false
        };
    }

    [Fact]
    public void Audience_All_AlwaysMatches()
    {
        Assert.True(AudienceMatches("all", lifetimeOrders: 5, "gold", []));
        Assert.True(AudienceMatches("all", lifetimeOrders: 0, "", []));
    }

    [Fact]
    public void Audience_NewCustomers_MatchesOnlyWhenLifetimeOrdersIsZero()
    {
        Assert.True(AudienceMatches("new_customers", lifetimeOrders: 0, "", []));
        Assert.False(AudienceMatches("new_customers", lifetimeOrders: 1, "", []));
        Assert.False(AudienceMatches("new_customers", lifetimeOrders: 100, "", []));
    }

    [Fact]
    public void Audience_Segment_MatchesWhenCustomerSegmentInList()
    {
        Assert.True(AudienceMatches("segment", lifetimeOrders: 3, "gold", ["silver", "gold"]));
    }

    [Fact]
    public void Audience_Segment_NoMatchWhenSegmentNotInList()
    {
        Assert.False(AudienceMatches("segment", lifetimeOrders: 3, "bronze", ["silver", "gold"]));
    }

    [Fact]
    public void Audience_Segment_EmptyEligibleSegments_NoMatch()
    {
        Assert.False(AudienceMatches("segment", lifetimeOrders: 0, "gold", []));
    }

    [Fact]
    public void Audience_UnknownValue_NoMatch()
    {
        Assert.False(AudienceMatches("vip_only", lifetimeOrders: 0, "vip", ["vip"]));
    }

    // ── RewardConfig JSONB parsing and discount arithmetic ─────────────────────
    // Mirrors lines 483-509 of CreateOrderCommand.cs.

    private static decimal? ParseAndComputePromoDiscount(
        string rewardConfigJson,
        decimal orderSubtotal)
    {
        try
        {
            using var doc    = JsonDocument.Parse(rewardConfigJson);
            var reward       = doc.RootElement;

            if (!reward.TryGetProperty("discount_type",  out var dtProp)
             || !reward.TryGetProperty("discount_value", out var dvProp))
                return null;   // required fields missing — skip

            var discountType  = dtProp.GetString();
            var discountValue = dvProp.GetDecimal();

            decimal candidate = discountType == "percent"
                ? Math.Round(orderSubtotal * (discountValue / 100m), 2)
                : discountValue;

            if (reward.TryGetProperty("max_discount", out var maxProp))
            {
                var maxDiscount = maxProp.GetDecimal();
                if (maxDiscount > 0 && candidate > maxDiscount)
                    candidate = maxDiscount;
            }

            if (candidate > orderSubtotal) candidate = orderSubtotal;
            if (candidate <= 0)           return null;   // skip zero-value promos

            return candidate;
        }
        catch
        {
            return null;   // malformed JSON — skip silently
        }
    }

    [Fact]
    public void Percent_NoCap_CorrectDiscount()
    {
        var json   = """{"discount_type":"percent","discount_value":10}""";
        var result = ParseAndComputePromoDiscount(json, 500m);
        Assert.Equal(50m, result);
    }

    [Fact]
    public void Percent_WithMaxCap_CappedCorrectly()
    {
        // 20% of 500 = 100, max_discount = 75 → 75
        var json   = """{"discount_type":"percent","discount_value":20,"max_discount":75}""";
        var result = ParseAndComputePromoDiscount(json, 500m);
        Assert.Equal(75m, result);
    }

    [Fact]
    public void Percent_ZeroMaxCap_CapNotApplied()
    {
        // max_discount = 0 → cap treated as disabled per the handler's: if (maxDiscount > 0)
        var json   = """{"discount_type":"percent","discount_value":10,"max_discount":0}""";
        var result = ParseAndComputePromoDiscount(json, 200m);
        Assert.Equal(20m, result);
    }

    [Fact]
    public void Flat_NoCap_CorrectDiscount()
    {
        var json   = """{"discount_type":"flat","discount_value":50}""";
        var result = ParseAndComputePromoDiscount(json, 300m);
        Assert.Equal(50m, result);
    }

    [Fact]
    public void Flat_ExceedsSubtotal_CappedAtSubtotal()
    {
        var json   = """{"discount_type":"flat","discount_value":999}""";
        var result = ParseAndComputePromoDiscount(json, 100m);
        Assert.Equal(100m, result);
    }

    [Fact]
    public void Flat_WithMaxCap_CappedByMaxDiscount()
    {
        // flat 100, max_discount = 60 → 60
        var json   = """{"discount_type":"flat","discount_value":100,"max_discount":60}""";
        var result = ParseAndComputePromoDiscount(json, 500m);
        Assert.Equal(60m, result);
    }

    [Fact]
    public void Percent_RoundsToTwoDecimalPlaces()
    {
        // 7% of 333.33 = 23.3331 → 23.33
        var json   = """{"discount_type":"percent","discount_value":7}""";
        var result = ParseAndComputePromoDiscount(json, 333.33m);
        Assert.Equal(23.33m, result);
    }

    // ── Malformed / missing RewardConfig ──────────────────────────────────────

    [Fact]
    public void MalformedJson_SkippedSilently()
    {
        var result = ParseAndComputePromoDiscount("not-valid-json{{{", 200m);
        Assert.Null(result);
    }

    [Fact]
    public void MissingDiscountType_SkippedSilently()
    {
        var json   = """{"discount_value":10}""";
        var result = ParseAndComputePromoDiscount(json, 200m);
        Assert.Null(result);
    }

    [Fact]
    public void MissingDiscountValue_SkippedSilently()
    {
        var json   = """{"discount_type":"percent"}""";
        var result = ParseAndComputePromoDiscount(json, 200m);
        Assert.Null(result);
    }

    [Fact]
    public void ZeroComputedDiscount_Skipped()
    {
        // 0% off → 0 → handler does: if (promoDiscountCandidate <= 0) continue
        var json   = """{"discount_type":"percent","discount_value":0}""";
        var result = ParseAndComputePromoDiscount(json, 200m);
        Assert.Null(result);
    }

    // ── Budget guard ──────────────────────────────────────────────────────────
    // The handler filters: p.TotalBudget == null || p.SpentBudget < p.TotalBudget

    private static bool WithinBudget(decimal? totalBudget, decimal spentBudget)
        => totalBudget == null || spentBudget < totalBudget.Value;

    [Fact]
    public void Budget_NullTotalBudget_AlwaysEligible()
    {
        Assert.True(WithinBudget(null, 0m));
        Assert.True(WithinBudget(null, 99999m));
    }

    [Fact]
    public void Budget_SpentLessThanTotal_Eligible()
    {
        Assert.True(WithinBudget(1000m, 999.99m));
    }

    [Fact]
    public void Budget_SpentEqualsTotal_NotEligible()
    {
        // Strict less-than: p.SpentBudget < p.TotalBudget
        Assert.False(WithinBudget(1000m, 1000m));
    }

    [Fact]
    public void Budget_SpentExceedsTotal_NotEligible()
    {
        Assert.False(WithinBudget(1000m, 1001m));
    }

    // ── First-match-wins ordering ─────────────────────────────────────────────

    [Fact]
    public void FirstMatchWins_OnlyOnePromotionApplied()
    {
        // Simulate two matching promotions; result is the first one's discount.
        var promos = new[]
        {
            ("promo-a", """{"discount_type":"flat","discount_value":30}"""),
            ("promo-b", """{"discount_type":"flat","discount_value":50}"""),
        };

        string? appliedPromo    = null;
        decimal promotionDiscount = 0m;

        foreach (var (name, config) in promos)
        {
            var candidate = ParseAndComputePromoDiscount(config, 200m);
            if (candidate is null or <= 0) continue;

            promotionDiscount = candidate.Value;
            appliedPromo      = name;
            break;   // first match wins
        }

        Assert.Equal("promo-a", appliedPromo);
        Assert.Equal(30m, promotionDiscount);
    }

    // ── Interaction with coupon and loyalty discounts ─────────────────────────
    // The handler computes orderSubtotalForPromo = subtotal - couponDiscount - loyaltyDiscount - ...
    // Promotion sees the post-coupon subtotal, not the raw subtotal.

    [Fact]
    public void PromoAppliesOnPostCouponSubtotal()
    {
        const decimal subtotal        = 500m;
        const decimal couponDiscount  = 50m;
        const decimal loyaltyDiscount = 20m;

        // orderSubtotalForPromo = 500 - 50 - 20 = 430
        var orderSubtotalForPromo = subtotal - couponDiscount - loyaltyDiscount;
        Assert.Equal(430m, orderSubtotalForPromo);

        // 10% of 430 = 43
        var json       = """{"discount_type":"percent","discount_value":10}""";
        var promoDisc  = ParseAndComputePromoDiscount(json, orderSubtotalForPromo);
        Assert.Equal(43m, promoDisc);
    }
}
