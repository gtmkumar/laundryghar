using System.Text.Json;
using FluentValidation;
using laundryghar.Orders.Application.Common;
using laundryghar.Orders.Application.Orders.Dtos;
using laundryghar.SharedDataModel.Entities.Commerce;
using Microsoft.Extensions.Options;
using MediatR;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;

namespace laundryghar.Orders.Application.Orders.Commands;

public sealed record CreateOrderCommand(
    CreateOrderRequest Request,
    Guid? ActorId,
    /// <summary>
    /// Client-supplied idempotency key (from <c>Idempotency-Key</c> header or request body).
    /// When provided, a second identical call within the same brand returns the already-created
    /// order without re-executing balance mutations (loyalty burn, package debit, etc.).
    /// Stored in <c>Order.Metadata</c> as <c>{"idempotency_key":"..."}</c>.
    /// </summary>
    string? ResolvedIdempotencyKey = null)
    : IRequest<OrderDto>;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;
    private readonly OrdersSettings _settings;

    public CreateOrderHandler(LaundryGharDbContext db, ICurrentUser user, IOptions<OrdersSettings> opts)
    {
        _db       = db;
        _user     = user;
        _settings = opts.Value;
    }

    public async Task<OrderDto> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // ── B2: Idempotency dedup guard ──────────────────────────────────────
        // When the caller supplies an idempotency key (POS double-tap, network retry),
        // return the already-created order without re-running any balance mutations.
        // Key is stored in Order.Metadata (jsonb) so no schema migration is required.
        //
        // DEF-A3 fix: string.Contains() on a jsonb column translates to LIKE on jsonb,
        // which Postgres rejects (42883 "operator does not exist: jsonb ~~ jsonb").
        // Use EF.Functions.JsonContains() which emits the @> containment operator,
        // e.g.: metadata @> '{"idempotency_key":"<key>"}'::jsonb — fully supported on jsonb.
        if (!string.IsNullOrWhiteSpace(cmd.ResolvedIdempotencyKey))
        {
            var idemTag  = cmd.ResolvedIdempotencyKey.Trim();
            var idemJson = $"{{\"idempotency_key\":\"{idemTag}\"}}";

            var existingOrder = await _db.Orders
                .Where(o => o.BrandId == brandId
                         && EF.Functions.JsonContains(o.Metadata, idemJson))
                .FirstOrDefaultAsync(ct);

            if (existingOrder is not null)
            {
                var existItems   = await _db.OrderItems
                    .Where(i => i.OrderId == existingOrder.Id && i.BrandId == brandId)
                    .ToListAsync(ct);
                var existAddons  = await _db.OrderAddons
                    .Where(a => a.OrderId == existingOrder.Id)
                    .ToListAsync(ct);
                var existHistory = await _db.OrderStatusHistories
                    .Where(h => h.OrderId == existingOrder.Id && h.BrandId == brandId)
                    .OrderBy(h => h.ChangedAt)
                    .ToListAsync(ct);
                return ToDto(existingOrder, existItems, existAddons, existHistory);
            }
        }

        // ── Validate store belongs to this brand ────────────────────────────
        var store = await _db.Stores
            .FirstOrDefaultAsync(s => s.Id == req.StoreId && s.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException($"Store {req.StoreId} not found in brand.");

        // ── Validate customer belongs to this brand (cross-brand IDOR guard) ─
        // req.CustomerId is actor-supplied in the request body; RLS does NOT protect
        // a bare assignment — we must verify ownership before writing the FK.
        var customerInBrand = await _db.Customers
            .AnyAsync(c => c.Id == req.CustomerId && c.BrandId == brandId, ct);
        if (!customerInBrand)
            throw new KeyNotFoundException("Customer not found.");

        // ── Resolve prices for each line ────────────────────────────────────
        decimal subtotal = 0, addonTotal = 0;
        var itemEntities  = new List<OrderItem>();
        var addonEntities = new List<OrderAddon>();

        // Collect per-service TAT hours to feed the TAT calculator after the loop.
        // Key: ServiceId → TAT hours (express or base depending on order type).
        var serviceTatMap = new Dictionary<Guid, int>();

        for (int i = 0; i < req.Items.Length; i++)
        {
            var line = req.Items[i];
            var resolved = await PriceResolver.ResolveAsync(
                _db, brandId, req.StoreId,
                line.ServiceId, line.ItemId, line.ItemVariantId, ct)
                ?? throw new BusinessRuleException(
                    $"No published price found for item {line.ItemId} / service {line.ServiceId}.");

            var unitPrice    = req.IsExpress && resolved.ExpressPrice.HasValue
                                   ? resolved.ExpressPrice.Value
                                   : resolved.BasePrice;
            var lineSubtotal = unitPrice * line.Quantity;
            subtotal += lineSubtotal;

            // Collect service TAT (deduplicated per service; one DB fetch per unique service)
            if (!serviceTatMap.ContainsKey(line.ServiceId))
            {
                var svc = await _db.Services
                    .AsNoTracking()
                    .Where(s => s.Id == line.ServiceId && s.BrandId == brandId)
                    .Select(s => new { s.BaseTatHours, s.ExpressTatHours })
                    .FirstOrDefaultAsync(ct);

                if (svc is not null)
                    serviceTatMap[line.ServiceId] = req.IsExpress ? svc.ExpressTatHours : svc.BaseTatHours;
            }

            itemEntities.Add(new OrderItem
            {
                Id                  = Guid.NewGuid(),
                // OrderId / OrderCreatedAt set after order is inserted
                BrandId             = brandId,
                StoreId             = req.StoreId,
                LineNumber          = (short)(i + 1),
                ServiceId           = line.ServiceId,
                ItemId              = line.ItemId,
                ItemVariantId       = line.ItemVariantId,
                PriceListItemId     = resolved.PriceListItemId,
                ItemNameSnapshot    = resolved.ItemNameSnapshot,
                ServiceNameSnapshot = resolved.ServiceNameSnapshot,
                UnitPrice           = unitPrice,
                Quantity            = line.Quantity,
                UnitOfMeasure       = "piece",
                LineSubtotal        = lineSubtotal,
                LineDiscount        = 0,
                LineAddonsTotal     = 0,
                LineTax             = 0,        // filled below
                LineTotal           = lineSubtotal,
                IsExpress           = req.IsExpress,
                Notes               = line.Notes,
                Metadata            = "{}",
                CreatedAt           = now,
                UpdatedAt           = now,
                CreatedBy           = cmd.ActorId,
                UpdatedBy           = cmd.ActorId,
                Status              = "active"
            });
        }

        // ── Compute promised delivery date (TAT engine) ─────────────────────
        // Uses MAX(service TAT hours) across all distinct services on the order.
        // Falls back to config defaults when catalog TAT is absent (see TatCalculator).
        var promisedDeliveryAt = TatCalculator.Compute(
            now, req.IsExpress, [.. serviceTatMap.Values], _settings);

        // ── Process add-ons ─────────────────────────────────────────────────
        foreach (var aReq in req.Addons)
        {
            var addon = await _db.AddOns
                .FirstOrDefaultAsync(a => a.Id == aReq.AddonId && a.BrandId == brandId, ct)
                ?? throw new KeyNotFoundException($"AddOn {aReq.AddonId} not found.");

            var charge = addon.PriceValue * aReq.Quantity;
            addonTotal += charge;

            Guid? parentItemId = aReq.OrderItemIndex.HasValue
                ? itemEntities[aReq.OrderItemIndex.Value].Id
                : null;

            addonEntities.Add(new OrderAddon
            {
                Id                = Guid.NewGuid(),
                BrandId           = brandId,
                OrderItemId       = parentItemId,
                AddonId           = aReq.AddonId,
                AddonNameSnapshot = addon.Name,
                PricingType       = addon.PricingType,
                UnitPrice         = addon.PriceValue,
                Quantity          = aReq.Quantity,
                TotalCharge       = charge,
                CreatedAt         = now,
                CreatedBy         = cmd.ActorId
            });

            // Update line addon total if item-level
            if (parentItemId.HasValue)
            {
                var li = itemEntities.First(x => x.Id == parentItemId.Value);
                li.LineAddonsTotal += charge;
                li.LineTotal       += charge;
            }
        }

        // ── Totals ──────────────────────────────────────────────────────────
        var expressSurcharge = req.IsExpress
            ? Math.Round(subtotal * (_settings.ExpressSurchargePercent / 100m), 2)
            : 0m;

        // ── Coupon resolution (server-side, mirrors Commerce ValidateApplyCouponHandler) ──
        // Validate eligibility + compute discount before taxable amount so the
        // discount reduces the tax base. Redemption row is inserted inside the
        // transaction below to remain atomic with the order insert.
        Coupon?          coupon          = null;
        decimal          couponDiscount  = 0m;
        CouponRedemption? couponRedemption = null;

        if (!string.IsNullOrWhiteSpace(req.CouponCode))
        {
            var now2 = DateTimeOffset.UtcNow;
            coupon = await _db.Coupons
                .FirstOrDefaultAsync(x => x.Code == req.CouponCode.ToUpperInvariant()
                                       && x.BrandId == brandId
                                       && x.DeletedAt == null, ct);

            if (coupon is null)
                throw new BusinessRuleException($"Coupon '{req.CouponCode}' not found.");
            if (coupon.Status != "active")
                throw new BusinessRuleException("Coupon is not active.");
            if (coupon.ValidFrom > now2)
                throw new BusinessRuleException("Coupon is not yet valid.");
            if (coupon.ValidUntil.HasValue && coupon.ValidUntil < now2)
                throw new BusinessRuleException("Coupon has expired.");
            if (coupon.MaxTotalUses.HasValue && coupon.CurrentUsageCount >= coupon.MaxTotalUses.Value)
                throw new BusinessRuleException("Coupon has reached its maximum global usage limit.");

            // Subtotal for minimum-order check (pre-coupon, pre-tax).
            var orderSubtotal = subtotal + addonTotal + expressSurcharge;
            if (orderSubtotal < coupon.MinOrderValue)
                throw new BusinessRuleException(
                    $"Order subtotal must be at least {coupon.MinOrderValue} to use this coupon.");

            // Per-customer usage
            var customerUsage = await _db.CouponRedemptions
                .CountAsync(r => r.CouponId == coupon.Id
                              && r.CustomerId == req.CustomerId
                              && r.RevertedAt == null, ct);

            if (coupon.IsSingleUsePerCust && customerUsage >= 1)
                throw new BusinessRuleException("This coupon can only be used once per customer.");
            if (customerUsage >= coupon.MaxUsesPerCustomer)
                throw new BusinessRuleException(
                    $"You have reached the maximum uses ({coupon.MaxUsesPerCustomer}) for this coupon.");

            // Compute discount
            couponDiscount = coupon.CouponType == "percent"
                ? Math.Round(orderSubtotal * (coupon.DiscountValue / 100m), 2)
                : coupon.DiscountValue;

            if (coupon.MaxDiscountAmount.HasValue && couponDiscount > coupon.MaxDiscountAmount.Value)
                couponDiscount = coupon.MaxDiscountAmount.Value;
            if (couponDiscount > orderSubtotal)
                couponDiscount = orderSubtotal;

            // Build redemption entity (FK filled after order ID is known)
            couponRedemption = new CouponRedemption
            {
                Id                    = Guid.NewGuid(),
                CouponId              = coupon.Id,
                BrandId               = brandId,
                CustomerId            = req.CustomerId,
                CouponCode            = coupon.Code,
                DiscountAmount        = couponDiscount,
                OrderSubtotalSnapshot = orderSubtotal,
                RedeemedAt            = now2,
                Metadata              = "{}",
                CreatedAt             = now2,
                CreatedBy             = req.CustomerId
            };
        }

        // ── Loyalty burn (redeem points for a discount) ─────────────────────
        // Only burns when:
        //   (a) customer requests it (LoyaltyPointsToRedeem > 0)
        //   (b) a program exists and is active
        //   (c) customer has sufficient balance (>= MinBurnPoints)
        //   (d) capped by MaxBurnPerOrderPct of the pre-loyalty subtotal
        decimal loyaltyDiscount   = 0m;
        int     pointsBurned      = 0;
        LoyaltyProgram?      loyaltyProgram    = null;
        LoyaltyPointsLedger? loyaltyDebitEntry = null;

        if (req.LoyaltyPointsToRedeem > 0)
        {
            loyaltyProgram = await _db.LoyaltyPrograms
                .FirstOrDefaultAsync(
                    lp => lp.BrandId == brandId && lp.IsActive && lp.Status == "active", ct);

            if (loyaltyProgram is not null)
            {
                // Re-use the tracked customer entity already checked for brand ownership above.
                var loyaltyCustomer = await _db.Customers
                    .FirstOrDefaultAsync(c => c.Id == req.CustomerId && c.BrandId == brandId, ct);

                if (loyaltyCustomer is not null
                    && loyaltyCustomer.LoyaltyPointsBalance >= loyaltyProgram.MinBurnPoints)
                {
                    var pointsToRedeem = Math.Min(
                        req.LoyaltyPointsToRedeem, loyaltyCustomer.LoyaltyPointsBalance);

                    // Monetary value of points being redeemed (BurnRate = ₹ per point)
                    var potentialDiscount = pointsToRedeem * loyaltyProgram.BurnRate;

                    // Cap: MaxBurnPerOrderPct of the pre-loyalty subtotal
                    var orderSubtotalForCap = subtotal + addonTotal + expressSurcharge - couponDiscount;
                    var maxAllowedDiscount  = loyaltyProgram.MaxBurnPerOrderPct > 0
                        ? orderSubtotalForCap * (loyaltyProgram.MaxBurnPerOrderPct / 100m)
                        : potentialDiscount;

                    loyaltyDiscount = Math.Min(potentialDiscount, maxAllowedDiscount);
                    loyaltyDiscount = Math.Round(loyaltyDiscount, 2);

                    // Back-calculate how many points are actually consumed
                    pointsBurned = loyaltyProgram.BurnRate > 0
                        ? (int)Math.Ceiling(loyaltyDiscount / loyaltyProgram.BurnRate)
                        : 0;

                    if (pointsBurned > 0 && loyaltyDiscount > 0)
                    {
                        var balanceBefore = loyaltyCustomer.LoyaltyPointsBalance;
                        var balanceAfter  = balanceBefore - pointsBurned;

                        loyaltyDebitEntry = new LoyaltyPointsLedger
                        {
                            Id               = Guid.NewGuid(),
                            BrandId          = brandId,
                            CustomerId       = req.CustomerId,
                            LoyaltyProgramId = loyaltyProgram.Id,
                            TransactionType  = "burn",
                            Direction        = -1,
                            Points           = pointsBurned,
                            BalanceBefore    = balanceBefore,
                            BalanceAfter     = balanceAfter,
                            MonetaryEquivalent = loyaltyDiscount,
                            // OrderId / OrderCreatedAt filled after order ID is known
                            Notes            = $"Redeemed {pointsBurned} points on order",
                            PerformedByType  = "customer",
                            PerformedBy      = cmd.ActorId,
                            OccurredAt       = now,
                            CreatedAt        = now,
                            CreatedBy        = cmd.ActorId
                        };

                        // Decrement customer balance (tracked entity — EF will UPDATE)
                        loyaltyCustomer.LoyaltyPointsBalance = balanceAfter;
                        loyaltyCustomer.UpdatedAt = now;
                        loyaltyCustomer.Version++;
                    }
                }
            }
        }

        // ── Package credit debit ─────────────────────────────────────────────────
        // If the request names a CustomerPackageId, or the customer has an active package
        // with remaining balance, apply the credit as a pre-tax discount.
        decimal          packageDiscount = 0m;
        CustomerPackage? activePackage   = null;
        PackageUsageLedger? packageDebit = null;

        if (req.CustomerPackageId.HasValue)
        {
            activePackage = await _db.CustomerPackages
                .Include(cp => cp.Package)
                .FirstOrDefaultAsync(cp => cp.Id == req.CustomerPackageId.Value
                                        && cp.CustomerId == req.CustomerId
                                        && cp.BrandId == brandId
                                        && cp.Status == "active", ct);
        }

        if (activePackage is null)
        {
            // Auto-resolve: pick the earliest-expiring active package with remaining balance.
            activePackage = await _db.CustomerPackages
                .Include(cp => cp.Package)
                .Where(cp => cp.CustomerId == req.CustomerId
                          && cp.BrandId == brandId
                          && cp.Status == "active"
                          && (cp.CreditValueRemaining ?? 0) > 0
                          && (cp.IsUnlimitedValidity || cp.ExpiresAt == null || cp.ExpiresAt > now))
                .OrderBy(cp => cp.ExpiresAt ?? DateTimeOffset.MaxValue)
                .FirstOrDefaultAsync(ct);
        }

        if (activePackage is not null && (activePackage.CreditValueRemaining ?? 0) > 0)
        {
            var orderGross  = subtotal + addonTotal + expressSurcharge - couponDiscount - loyaltyDiscount;
            var available   = activePackage.CreditValueRemaining ?? 0m;
            packageDiscount = Math.Min(available, orderGross);
            packageDiscount = Math.Round(packageDiscount, 2);

            if (packageDiscount > 0)
            {
                var balanceBefore = available;
                var balanceAfter  = balanceBefore - packageDiscount;

                packageDebit = new PackageUsageLedger
                {
                    Id                = Guid.NewGuid(),
                    CustomerPackageId = activePackage.Id,
                    BrandId           = brandId,
                    CustomerId        = req.CustomerId,
                    // OrderId / OrderCreatedAt filled after order is created
                    TransactionType   = "debit",
                    Amount            = packageDiscount,
                    BalanceBefore     = balanceBefore,
                    BalanceAfter      = balanceAfter,
                    Notes             = "Package credit applied at order create",
                    ReferenceType     = "order",
                    PerformedBy       = cmd.ActorId,
                    OccurredAt        = now,
                    CreatedAt         = now,
                    CreatedBy         = cmd.ActorId
                };

                // Decrement balance on tracked entity — EF will emit UPDATE.
                // CreditValueRemaining is GENERATED ALWAYS and must not be written.
                activePackage.CreditValueUsed += packageDiscount;
                activePackage.UsageCount++;
                activePackage.LastUsedAt = now;
                activePackage.UpdatedAt  = now;
                activePackage.UpdatedBy  = cmd.ActorId;
            }
        }

        // ── Promotions evaluation ────────────────────────────────────────────────
        // Evaluate active promotions for first-order + audience eligibility.
        // Only one promotion applies (first match wins). Promotions are CRUD-only
        // until this wiring — this makes them functional.
        decimal    promotionDiscount = 0m;
        Promotion? appliedPromotion  = null;

        // Load the customer's lifetime orders count for first-order check.
        // Using a separate AsNoTracking projection to avoid disturbing the tracked
        // loyaltyCustomer entity loaded above.
        var customerForPromo = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == req.CustomerId && c.BrandId == brandId)
            .Select(c => new { c.LifetimeOrders, c.CustomerSegment })
            .FirstOrDefaultAsync(ct);

        var promoNow = DateTimeOffset.UtcNow;
        var activePromotions = await _db.Promotions
            .Where(p => p.BrandId == brandId
                     && p.Status == "active"
                     && p.ValidFrom <= promoNow
                     && (p.ValidUntil == null || p.ValidUntil >= promoNow)
                     && (p.TotalBudget == null || p.SpentBudget < p.TotalBudget))
            .OrderBy(p => p.CreatedAt)   // deterministic: oldest first
            .Take(20)
            .ToListAsync(ct);

        if (customerForPromo is not null)
        {
            var orderSubtotalForPromo = subtotal + addonTotal + expressSurcharge
                                        - couponDiscount - loyaltyDiscount - packageDiscount;

            foreach (var promo in activePromotions)
            {
                // Audience check
                bool audienceMatch = promo.TargetAudience switch
                {
                    "all"           => true,
                    "new_customers" => customerForPromo.LifetimeOrders == 0,
                    "segment"       => promo.EligibleSegments != null
                                       && promo.EligibleSegments.Contains(
                                              customerForPromo.CustomerSegment ?? ""),
                    _               => false
                };
                if (!audienceMatch) continue;

                // Parse RewardConfig (jsonb) to get discount type and value.
                // Expected shape: { "discount_type": "percent"|"flat", "discount_value": 10, "max_discount": 100 }
                decimal promoDiscountCandidate = 0m;
                try
                {
                    using var rewardDoc = System.Text.Json.JsonDocument.Parse(promo.RewardConfig);
                    var reward = rewardDoc.RootElement;

                    if (!reward.TryGetProperty("discount_type", out var dtProp)
                        || !reward.TryGetProperty("discount_value", out var dvProp)) continue;

                    var discountType  = dtProp.GetString();
                    var discountValue = dvProp.GetDecimal();

                    promoDiscountCandidate = discountType == "percent"
                        ? Math.Round(orderSubtotalForPromo * (discountValue / 100m), 2)
                        : discountValue;

                    // Optional max cap
                    if (reward.TryGetProperty("max_discount", out var maxProp))
                    {
                        var maxDiscount = maxProp.GetDecimal();
                        if (maxDiscount > 0 && promoDiscountCandidate > maxDiscount)
                            promoDiscountCandidate = maxDiscount;
                    }

                    if (promoDiscountCandidate > orderSubtotalForPromo)
                        promoDiscountCandidate = orderSubtotalForPromo;
                    if (promoDiscountCandidate <= 0) continue;
                }
                catch { continue; }   // malformed RewardConfig — skip silently

                promotionDiscount = promoDiscountCandidate;
                appliedPromotion  = promo;
                break;   // first match wins
            }
        }

        var taxableAmount = subtotal + addonTotal + expressSurcharge
                           - couponDiscount - loyaltyDiscount - packageDiscount - promotionDiscount;
        if (taxableAmount < 0m) taxableAmount = 0m;

        var halfRate      = _settings.TaxRatePercent / 2m;
        var taxTotal      = Math.Round(taxableAmount * (_settings.TaxRatePercent / 100m), 2);
        var cgst          = Math.Round(taxableAmount * (halfRate / 100m), 2);
        var sgst          = taxTotal - cgst;
        var grandTotal    = taxableAmount + taxTotal;

        // Update per-line tax proportionally
        foreach (var li in itemEntities)
        {
            li.LineTax   = Math.Round(li.LineSubtotal * (_settings.TaxRatePercent / 100m), 2);
            li.LineTotal = li.LineSubtotal + li.LineAddonsTotal + li.LineTax;
        }

        // ── Order number: LG-{yyyy}-{storeCode}-{seq} ───────────────────────
        // Atomic per-(brand,store,year) allocator — race-free under concurrency.
        var orderNumber = await GenerateOrderNumberAsync(brandId, req.StoreId, store.Code, now.Year, ct);

        // ── Insert Order ────────────────────────────────────────────────────
        var order = new Order
        {
            Id               = Guid.NewGuid(),
            CreatedAt        = now,   // EF will write this; partition key
            OrderNumber      = orderNumber,
            BrandId          = brandId,
            FranchiseId      = store.FranchiseId,
            StoreId          = req.StoreId,
            CustomerId       = req.CustomerId,
            PickupAddressId  = req.PickupAddressId,
            DeliveryAddressId = req.DeliveryAddressId,
            Channel          = req.Channel,
            OrderType        = req.IsExpress ? "express" : "standard",
            IsExpress        = req.IsExpress,
            RequiresPickup   = req.RequiresPickup,
            RequiresDelivery = req.RequiresDelivery,
            Subtotal         = subtotal,
            AddonTotal       = addonTotal,
            ExpressSurcharge = expressSurcharge,
            PickupCharge     = 0,
            DeliveryCharge   = 0,
            DiscountTotal    = couponDiscount + loyaltyDiscount + packageDiscount + promotionDiscount,
            CouponDiscount   = couponDiscount,
            LoyaltyDiscount  = loyaltyDiscount,
            LoyaltyPointsUsed = pointsBurned,
            PackageDiscount  = packageDiscount,
            CustomerPackageId = activePackage?.Id,
            PackageId        = activePackage?.PackageId,
            CouponId         = coupon?.Id,
            CouponCode       = coupon?.Code,
            TaxableAmount    = taxableAmount,
            TaxTotal         = taxTotal,
            Cgst             = cgst,
            Sgst             = sgst,
            Igst             = 0,
            RoundOff         = 0,
            GrandTotal       = grandTotal,
            AmountPaid       = 0,
            RefundedAmount   = 0,
            CurrencyCode     = _settings.DefaultCurrencyCode,
            TotalItems       = req.Items.Length,
            TotalGarments    = 0,
            Status              = OrderStatus.Placed,
            PaymentStatus       = "pending",
            PlacedAt            = now,
            PromisedDeliveryAt  = promisedDeliveryAt,
            NotesCustomer       = req.NotesCustomer,
            // Embed idempotency key in Metadata so dedup guard above can find it without
            // a dedicated column. Pattern: {"idempotency_key":"<key>"}
            Metadata            = string.IsNullOrWhiteSpace(cmd.ResolvedIdempotencyKey)
                                      ? "{}"
                                      : $"{{\"idempotency_key\":\"{cmd.ResolvedIdempotencyKey.Trim()}\"}}",
            UpdatedAt        = now,
            CreatedBy        = cmd.ActorId,
            UpdatedBy        = cmd.ActorId,
            Version          = 1
        };

        // Fix child FK references
        foreach (var li in itemEntities)
        {
            li.OrderId        = order.Id;
            li.OrderCreatedAt = order.CreatedAt;
        }
        foreach (var a in addonEntities)
        {
            a.OrderId        = order.Id;
            a.OrderCreatedAt = order.CreatedAt;
        }

        // ── Status history (placed) ─────────────────────────────────────────
        var history = new OrderStatusHistory
        {
            Id              = Guid.NewGuid(),
            OrderId         = order.Id,
            OrderCreatedAt  = order.CreatedAt,
            BrandId         = brandId,
            FromStatus      = null,
            ToStatus        = OrderStatus.Placed,
            ChangedAt       = now,
            ChangedByType   = "user",
            ChangedById     = cmd.ActorId,
            CustomerNotified = false,
            Metadata        = "{}",
            CreatedAt       = now,
            CreatedBy       = cmd.ActorId
        };

        // ── Outbox event (order.placed) ─────────────────────────────────────
        var outbox = new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "order",
            AggregateId   = order.Id,
            EventType     = "order.placed",
            EventVersion  = 1,
            Payload       = JsonSerializer.Serialize(new
            {
                orderId            = order.Id,
                orderNumber        = order.OrderNumber,
                brandId            = brandId,
                storeId            = req.StoreId,
                customerId         = req.CustomerId,
                grandTotal         = grandTotal,
                currency           = _settings.DefaultCurrencyCode,
                placedAt           = now,
                promisedDeliveryAt = promisedDeliveryAt,
                promotionId        = appliedPromotion?.Id,
                promotionDiscount  = promotionDiscount
            }),
            Metadata      = "{}",
            OccurredAt    = now,
            Status        = "pending",
            CreatedAt     = now,
            CreatedBy     = cmd.ActorId
        };

        // ── Single transaction (wrapped in execution strategy for Npgsql retry compat) ──
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            _db.Orders.Add(order);
            _db.OrderItems.AddRange(itemEntities);
            _db.OrderAddons.AddRange(addonEntities);
            _db.OrderStatusHistories.Add(history);
            _db.OutboxEvents.Add(outbox);

            // Coupon: insert redemption + increment usage count atomically with the order.
            if (couponRedemption is not null && coupon is not null)
            {
                couponRedemption.OrderId        = order.Id;
                couponRedemption.OrderCreatedAt = order.CreatedAt;
                _db.CouponRedemptions.Add(couponRedemption);
                coupon.CurrentUsageCount++;
                coupon.UpdatedAt = now;
            }

            // Loyalty burn: debit ledger entry atomically with the order.
            if (loyaltyDebitEntry is not null)
            {
                loyaltyDebitEntry.OrderId        = order.Id;
                loyaltyDebitEntry.OrderCreatedAt = order.CreatedAt;
                _db.LoyaltyPointsLedger.Add(loyaltyDebitEntry);
            }

            // Package credit: debit ledger entry + updated CustomerPackage balance atomically with the order.
            // activePackage is already tracked (loaded via FirstOrDefaultAsync); EF will UPDATE it.
            if (packageDebit is not null)
            {
                packageDebit.OrderId        = order.Id;
                packageDebit.OrderCreatedAt = order.CreatedAt;
                _db.PackageUsageLedger.Add(packageDebit);
            }

            // Promotion: increment redemption metrics on the promotion entity atomically with the order.
            // appliedPromotion was loaded as a tracked entity (ToListAsync above is tracking by default).
            if (appliedPromotion is not null)
            {
                appliedPromotion.RedemptionsCount++;
                appliedPromotion.SpentBudget += promotionDiscount;
                appliedPromotion.UpdatedAt    = now;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return ToDto(order, itemEntities, addonEntities, [history]);
    }

    private async Task<string> GenerateOrderNumberAsync(
        Guid brandId, Guid storeId, string storeCode, int year, CancellationToken ct)
    {
        // Delegates to order_lifecycle.next_order_number(...) which atomically
        // increments a per-(brand,store,year) counter (INSERT ... ON CONFLICT DO
        // UPDATE ... RETURNING). This eliminates the COUNT(*)+1 race where two
        // concurrent CreateOrder requests minted identical order numbers.
        // Runs on the request connection, so RLS sees the caller's brand context.
        return await _db.Database
            .SqlQuery<string>(
                $"SELECT order_lifecycle.next_order_number({brandId}, {storeId}, {storeCode}, {year}) AS \"Value\"")
            .SingleAsync(ct);
    }

    /// <param name="includeDeliveryOtp">
    /// Pass <c>true</c> only for customer-self queries. Exposes <c>DeliveryOtp</c>
    /// when status is <c>out_for_delivery</c>; null otherwise. Admin and staff callers
    /// should leave this <c>false</c> (default) — they use different UI flows and have
    /// no need for the rider-handoff OTP.
    /// </param>
    internal static OrderDto ToDto(
        Order o,
        IEnumerable<OrderItem>? items = null,
        IEnumerable<OrderAddon>? addons = null,
        IEnumerable<OrderStatusHistory>? history = null,
        bool includeDeliveryOtp = false)
    {
        // Derive promotion discount as the residual after named discount components.
        // For historical orders placed before this feature PromotionDiscount evaluates to 0.
        var derivedPromotionDiscount = Math.Max(
            0m, o.DiscountTotal - o.CouponDiscount - o.LoyaltyDiscount - o.PackageDiscount);

        // H4: only the owning customer sees the delivery OTP, and only while the
        // order is out for delivery. Reveal it by passing includeDeliveryOtp=true
        // from customer-self query handlers.
        var deliveryOtp = includeDeliveryOtp && o.Status == "out_for_delivery"
            ? o.DeliveryOtp
            : null;

        return new(
            o.Id, o.CreatedAt, o.OrderNumber, o.BrandId, o.StoreId, o.CustomerId,
            o.Channel, o.OrderType, o.IsExpress,
            o.Subtotal, o.AddonTotal, o.ExpressSurcharge, o.TaxTotal, o.Cgst, o.Sgst,
            o.DiscountTotal,
            derivedPromotionDiscount,
            o.GrandTotal, o.AmountPaid, o.AmountDue, o.CurrencyCode,
            o.TotalItems, o.Status, o.PaymentStatus, o.PlacedAt, o.UpdatedAt,
            o.PromisedDeliveryAt,
            items?.Select(i => new OrderItemDto(
                i.Id, i.ServiceId, i.ItemId, i.ItemVariantId,
                i.ItemNameSnapshot, i.ServiceNameSnapshot,
                i.UnitPrice, i.Quantity, i.UnitOfMeasure,
                i.LineSubtotal, i.LineTotal, i.Status)).ToList(),
            addons?.Select(a => new OrderAddonDto(
                a.Id, a.OrderItemId, a.AddonId, a.AddonNameSnapshot,
                a.PricingType, a.UnitPrice, a.Quantity, a.TotalCharge)).ToList(),
            history?.Select(h => new OrderStatusHistoryDto(
                h.Id, h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedByType,
                h.Reason, h.CustomerNotified)).ToList(),
            o.Rating,
            o.RatingComment,
            o.RatedAt,
            deliveryOtp);
    }
}

public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    private static readonly string[] AllowedChannels = ["walkin","app","whatsapp","call","web","pos"];

    public CreateOrderValidator()
    {
        RuleFor(x => x.Request.CustomerId).NotEmpty();
        RuleFor(x => x.Request.StoreId).NotEmpty();
        RuleFor(x => x.Request.Channel).NotEmpty()
            .Must(c => AllowedChannels.Contains(c))
            .WithMessage($"Channel must be one of: {string.Join(", ", AllowedChannels)}.");
        RuleFor(x => x.Request.Items).NotEmpty().WithMessage("At least one item is required.");
        RuleForEach(x => x.Request.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ItemId).NotEmpty();
            item.RuleFor(i => i.ServiceId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}
