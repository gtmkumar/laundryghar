using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace laundryghar.Commerce.Infrastructure.Seeders;

/// <summary>
/// Idempotent commerce seeder — Development only.
/// Seeds under LG-MAIN brand:
///   4 payment_methods (UPI / CARD / COD / WALLET)
///   3 packages      (Silver / Gold / Diamond)
///   1 loyalty_program
///   2 coupons       (WELCOME10 / FLAT50)
/// Check-before-insert on natural keys; prod-guarded.
/// </summary>
public sealed class CommerceSeeder
{
    private readonly LaundryGharDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly ILogger<CommerceSeeder> _logger;

    public CommerceSeeder(LaundryGharDbContext db, IHostEnvironment env, ILogger<CommerceSeeder> logger)
    {
        _db     = db;
        _env    = env;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment())
            throw new InvalidOperationException(
                "CommerceSeeder may only run in Development. Use a controlled bootstrap process for other environments.");

        _logger.LogInformation("Running commerce seeder...");

        var brand = await _db.Brands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Code == "LG-MAIN", ct);

        if (brand is null)
        {
            _logger.LogWarning("Brand LG-MAIN not found. Run Identity seeder first. Skipping commerce seed.");
            return;
        }

        var brandId = brand.Id;
        var now     = DateTimeOffset.UtcNow;

        await SeedPaymentMethodsAsync(brandId, now, ct);
        await SeedPackagesAsync(brandId, now, ct);
        await SeedLoyaltyProgramAsync(brandId, now, ct);
        await SeedCouponsAsync(brandId, now, ct);

        _logger.LogInformation("Commerce seeding complete.");
    }

    // ── Payment Methods ────────────────────────────────────────────────────────

    private async Task SeedPaymentMethodsAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.PaymentMethods.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .Select(x => x.Code)
            .ToHashSetAsync(ct);

        void Ensure(string code, string name, string methodType, bool isOnline, bool isRefundable, short order)
        {
            if (existing.Contains(code)) return;
            _db.PaymentMethods.Add(new PaymentMethod
            {
                Id           = Guid.NewGuid(),
                BrandId      = brandId,
                Code         = code,
                Name         = name,
                NameLocalized = $"{{\"en\":\"{name}\"}}",
                MethodType   = methodType,
                IsOnline     = isOnline,
                IsRefundable = isRefundable,
                IsActive     = true,
                DisplayOrder = order,
                Config       = "{}",
                Status       = "active",
                CreatedAt    = now,
                UpdatedAt    = now
            });
            existing.Add(code);
        }

        Ensure("UPI",    "UPI Payment",      "upi",    true,  true,  1);
        Ensure("CARD",   "Credit/Debit Card", "card",   true,  true,  2);
        Ensure("COD",    "Cash on Delivery",  "cod",    false, false, 3);
        Ensure("WALLET", "Wallet",            "wallet", false, true,  4);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded payment methods.");
    }

    // ── Packages ───────────────────────────────────────────────────────────────

    private async Task SeedPackagesAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.Packages.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .Select(x => x.Code)
            .ToHashSetAsync(ct);

        void Ensure(string code, string name, string tier, decimal price, decimal creditValue, decimal discount, decimal multiplier, int? validityDays, short order)
        {
            if (existing.Contains(code)) return;
            _db.Packages.Add(new Package
            {
                Id                  = Guid.NewGuid(),
                BrandId             = brandId,
                Code                = code,
                Name                = name,
                NameLocalized       = $"{{\"en\":\"{name}\"}}",
                Tier                = tier,
                Price               = price,
                CreditValue         = creditValue,
                DiscountPercent     = discount,
                CreditMultiplier    = multiplier,
                ValidityDays        = validityDays,
                IsUnlimitedValidity = validityDays is null,
                ApplicableServices  = [],
                ExcludedServices    = [],
                DisplayOrder        = order,
                IsFeatured          = order == 2,
                Status              = "active",
                CreatedAt           = now,
                UpdatedAt           = now,
                Version             = 1
            });
            existing.Add(code);
        }

        Ensure("PKG-SILVER",  "Silver Package",  "silver",  999m,  1100m,  10m, 1.10m, 90,   1);
        Ensure("PKG-GOLD",    "Gold Package",    "gold",   1999m,  2400m,  15m, 1.20m, 180,  2);
        Ensure("PKG-DIAMOND", "Diamond Package", "diamond", 4999m, 6250m,  20m, 1.25m, 365,  3);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded packages.");
    }

    // ── Loyalty Program ────────────────────────────────────────────────────────

    private async Task SeedLoyaltyProgramAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var exists = await _db.LoyaltyPrograms.IgnoreQueryFilters()
            .AnyAsync(x => x.BrandId == brandId, ct);
        if (exists) return;

        _db.LoyaltyPrograms.Add(new LoyaltyProgram
        {
            Id                    = Guid.NewGuid(),
            BrandId               = brandId,
            Code                  = "LG-REWARDS",
            Name                  = "Laundry Ghar Rewards",
            Description           = "Earn and redeem reward points on every order.",
            IsActive              = true,
            EarnRate              = 1m,      // 1 point per ₹1 spent
            EarnBasis             = "spend",
            BurnRate              = 0.25m,   // 1 point = ₹0.25
            MinBurnPoints         = 100,
            MaxBurnPerOrderPct    = 20m,
            MinOrderForEarn       = 100m,
            ExcludedServices      = [],
            PointExpiryMonths     = 12,
            WelcomeBonus          = 50,
            ReferralBonusReferrer = 100,
            ReferralBonusReferee  = 50,
            BirthdayBonus         = 200,
            TierConfig            = "{\"bronze\":{\"min\":0,\"max\":999},\"silver\":{\"min\":1000,\"max\":4999},\"gold\":{\"min\":5000,\"max\":null}}",
            Terms                 = "Points are non-transferable and expire 12 months after earning.",
            LaunchedAt            = now,
            Status                = "active",
            CreatedAt             = now,
            UpdatedAt             = now
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded loyalty program.");
    }

    // ── Coupons ────────────────────────────────────────────────────────────────

    private async Task SeedCouponsAsync(Guid brandId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _db.Coupons.IgnoreQueryFilters()
            .Where(x => x.BrandId == brandId)
            .Select(x => x.Code)
            .ToHashSetAsync(ct);

        void Ensure(string code, string name, string couponType, decimal discountValue, decimal? maxDiscount,
                    decimal minOrder, bool isFirstOrderOnly, bool isSingleUse, int? maxTotal, short maxPerCust)
        {
            if (existing.Contains(code)) return;
            _db.Coupons.Add(new Coupon
            {
                Id                   = Guid.NewGuid(),
                BrandId              = brandId,
                Code                 = code,
                Name                 = name,
                CouponType           = couponType,
                DiscountValue        = discountValue,
                MaxDiscountAmount    = maxDiscount,
                MinOrderValue        = minOrder,
                ApplicableServices   = [],
                ApplicableStores     = [],
                ApplicableFranchises = [],
                CustomerEligibility  = isFirstOrderOnly ? "new" : "all",
                IsFirstOrderOnly     = isFirstOrderOnly,
                IsSingleUsePerCust   = isSingleUse,
                MaxTotalUses         = maxTotal,
                MaxUsesPerCustomer   = maxPerCust,
                CurrentUsageCount    = 0,
                IsStackable          = false,
                IsPublic             = true,
                IsAutoApply          = false,
                ValidFrom            = now,
                ValidUntil           = now.AddYears(1),
                Status               = "active",
                CreatedAt            = now,
                UpdatedAt            = now
            });
            existing.Add(code);
        }

        Ensure("WELCOME10", "10% off for new customers", "percent", 10m, 200m, 100m, true,  true,  null, 1);
        Ensure("FLAT50",    "Flat ₹50 off",              "flat",        50m, null, 300m, false, false, 500,  3);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded coupons.");
    }
}
