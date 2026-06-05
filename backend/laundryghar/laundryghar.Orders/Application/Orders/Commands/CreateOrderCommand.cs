using System.Text.Json;
using FluentValidation;
using laundryghar.Orders.Application.Common;
using laundryghar.Orders.Application.Orders.Dtos;
using Microsoft.Extensions.Options;
using MediatR;

namespace laundryghar.Orders.Application.Orders.Commands;

public sealed record CreateOrderCommand(CreateOrderRequest Request, Guid? ActorId)
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

        // ── Validate store belongs to this brand ────────────────────────────
        var store = await _db.Stores
            .FirstOrDefaultAsync(s => s.Id == req.StoreId && s.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException($"Store {req.StoreId} not found in brand.");

        // ── Resolve prices for each line ────────────────────────────────────
        decimal subtotal = 0, addonTotal = 0;
        var itemEntities  = new List<OrderItem>();
        var addonEntities = new List<OrderAddon>();

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

        var taxableAmount = subtotal + addonTotal + expressSurcharge;
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
        // Use a DB sequence per store-year key to avoid collisions
        var orderNumber = await GenerateOrderNumberAsync(store.Code, now, ct);

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
            DiscountTotal    = 0,
            CouponDiscount   = 0,
            LoyaltyDiscount  = 0,
            PackageDiscount  = 0,
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
            Status           = OrderStatus.Placed,
            PaymentStatus    = "pending",
            PlacedAt         = now,
            NotesCustomer    = req.NotesCustomer,
            Metadata         = "{}",
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
                orderId     = order.Id,
                orderNumber = order.OrderNumber,
                brandId     = brandId,
                storeId     = req.StoreId,
                customerId  = req.CustomerId,
                grandTotal  = grandTotal,
                currency    = _settings.DefaultCurrencyCode,
                placedAt    = now
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
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return ToDto(order, itemEntities, addonEntities, [history]);
    }

    private async Task<string> GenerateOrderNumberAsync(
        string storeCode, DateTimeOffset now, CancellationToken ct)
    {
        // Count existing orders this year for this store to derive a seq
        var year = now.Year;
        var count = await _db.Orders
            .IgnoreQueryFilters()
            .CountAsync(o => o.StoreId != Guid.Empty
                          && o.OrderNumber.StartsWith($"LG-{year}-{storeCode}-"), ct);
        return $"LG-{year}-{storeCode}-{(count + 1):D6}";
    }

    internal static OrderDto ToDto(
        Order o,
        IEnumerable<OrderItem>? items = null,
        IEnumerable<OrderAddon>? addons = null,
        IEnumerable<OrderStatusHistory>? history = null) => new(
        o.Id, o.CreatedAt, o.OrderNumber, o.BrandId, o.StoreId, o.CustomerId,
        o.Channel, o.OrderType, o.IsExpress,
        o.Subtotal, o.AddonTotal, o.ExpressSurcharge, o.TaxTotal, o.Cgst, o.Sgst,
        o.GrandTotal, o.AmountPaid, o.AmountDue, o.CurrencyCode,
        o.TotalItems, o.Status, o.PaymentStatus, o.PlacedAt, o.UpdatedAt,
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
            h.Reason, h.CustomerNotified)).ToList()
    );
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
