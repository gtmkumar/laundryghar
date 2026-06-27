using System.Text.Json;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Crypto;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using operations.Application.Common.Interfaces;
using operations.Application.Fulfillment;
using operations.Application.Orders.Common;
using operations.Application.Orders.Orders.Dtos;

namespace operations.Application.Orders.Orders.Commands;

/// <summary>
/// Customer-facing parcel (point-to-point) order creation. Unlike laundry — where the
/// customer schedules a pickup and the order is created after weighing — a parcel has a
/// fixed, fare-quoted price, so booking creates the ORDER directly (job_type='parcel').
///
/// Brand + customer come from the JWT (never the body). The fare quote token locks the
/// price. A linked pickup_request (status='pending', tier-tagged) is created so the Wave 1.4
/// auto-dispatcher offers/assigns a rider and the rider task flow advances the order.
/// </summary>
public sealed record CreateParcelOrderCommand(Guid CustomerId, Guid BrandId, CreateParcelOrderRequest Request)
    : ICommand<OrderDto>;

public sealed class CreateParcelOrderHandler : ICommandHandler<CreateParcelOrderCommand, OrderDto>
{
    private readonly IOperationsDbContext _db;
    private readonly OrdersSettings _settings;
    private readonly IFieldCipher _cipher;
    private readonly IFulfillmentStrategyResolver _strategies;

    public CreateParcelOrderHandler(
        IOperationsDbContext db, IOptions<OrdersSettings> opts, IFieldCipher cipher,
        IFulfillmentStrategyResolver strategies)
    {
        _db = db;
        _settings = opts.Value;
        _cipher = cipher;
        _strategies = strategies;
    }

    public async Task<OrderDto> HandleAsync(CreateParcelOrderCommand cmd, CancellationToken ct)
    {
        var brandId = cmd.BrandId;
        var req = cmd.Request;
        var now = DateTimeOffset.UtcNow;

        // ── Verify the fare quote token (binding price) ─────────────────────
        var quote = FareQuoteToken.Verify(_cipher, req.FareQuoteToken, now);
        if (quote is null)
            throw new BusinessRuleException("Fare quote is missing, invalid, or expired. Request a fresh quote.");
        if (quote.PickupAddressId != req.PickupAddressId
            || quote.DeliveryAddressId != req.DeliveryAddressId
            || quote.Tier != req.VehicleTier)
            throw new BusinessRuleException("Fare quote does not match this order's addresses or vehicle tier.");

        // ── Validate addresses belong to this customer/brand (IDOR) ─────────
        var pickupAddr = await _db.CustomerAddresses
            .FirstOrDefaultAsync(a => a.Id == req.PickupAddressId
                                   && a.CustomerId == cmd.CustomerId && a.BrandId == brandId, ct)
            ?? throw new KeyNotFoundException("Pickup address not found.");
        var dropExists = await _db.CustomerAddresses
            .AnyAsync(a => a.Id == req.DeliveryAddressId
                        && a.CustomerId == cmd.CustomerId && a.BrandId == brandId, ct);
        if (!dropExists) throw new KeyNotFoundException("Drop address not found.");

        // ── Resolve a store (NOT NULL FK): pickup's serviceable store, else brand default ──
        var store = pickupAddr.ServiceableStoreId is Guid ssid
            ? await _db.Stores.FirstOrDefaultAsync(s => s.Id == ssid && s.BrandId == brandId && s.Status == "active", ct)
            : null;
        store ??= await _db.Stores
            .Where(s => s.BrandId == brandId && s.Status == "active")
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("No active store available to service this parcel.");

        var pickupCharge = quote.PickupCharge;
        var deliveryCharge = quote.DeliveryCharge;
        var grandTotal = pickupCharge + deliveryCharge;

        var orderNumber = await _db.SqlQueryScalarAsync<string>(
            $"SELECT order_lifecycle.next_order_number({brandId}, {store.Id}, {store.Code}, {now.Year}) AS \"Value\"",
            ct);

        // A parcel is a point_to_point trip — leg topology, initial status and its lifecycle
        // super-state are owned by the strategy, not hardcoded here (parity with CreateOrder).
        var strategy = _strategies.Resolve(laundryghar.SharedDataModel.Enums.FulfillmentMode.PointToPoint);
        var legs = strategy.ResolveLegs(requestedPickup: true, requestedDelivery: true);
        var initialStatus = strategy.InitialStatus;

        var order = new Order
        {
            Id                   = Guid.NewGuid(),
            CreatedAt            = now,
            OrderNumber          = orderNumber,
            BrandId              = brandId,
            FranchiseId          = store.FranchiseId,
            StoreId              = store.Id,
            CustomerId           = cmd.CustomerId,
            PickupAddressId      = req.PickupAddressId,
            DeliveryAddressId    = req.DeliveryAddressId,
            Channel              = "app",
            JobType              = JobType.Parcel,
            FulfillmentMode      = strategy.FulfillmentMode,
            RequestedVehicleTier = req.VehicleTier,
            OrderType            = "standard",
            IsExpress            = false,
            RequiresPickup       = legs.RequiresPickup,
            RequiresDelivery     = legs.RequiresDelivery,
            Subtotal             = 0,
            AddonTotal           = 0,
            ExpressSurcharge     = 0,
            PickupCharge         = pickupCharge,
            DeliveryCharge       = deliveryCharge,
            DiscountTotal        = 0,
            TaxableAmount        = 0,
            TaxTotal             = 0,
            Cgst = 0, Sgst = 0, Igst = 0, RoundOff = 0,
            GrandTotal           = grandTotal,
            AmountPaid           = 0,
            RefundedAmount       = 0,
            CurrencyCode         = _settings.DefaultCurrencyCode,
            TotalItems           = 0,
            TotalGarments        = 0,
            Status               = initialStatus,
            LifecycleState       = strategy.LifecycleStateFor(initialStatus),
            PaymentStatus        = "pending",
            PlacedAt             = now,
            NotesCustomer        = req.NotesCustomer,
            Metadata             = "{}",
            UpdatedAt            = now,
            CreatedBy            = cmd.CustomerId,
            UpdatedBy            = cmd.CustomerId,
            Version              = 1
        };

        var history = new OrderStatusHistory
        {
            Id               = Guid.NewGuid(),
            OrderId          = order.Id,
            OrderCreatedAt   = order.CreatedAt,
            BrandId          = brandId,
            FromStatus       = null,
            ToStatus         = initialStatus,
            ChangedAt        = now,
            ChangedByType    = "customer",
            ChangedById      = cmd.CustomerId,
            CustomerNotified = false,
            Metadata         = "{}",
            CreatedAt        = now,
            CreatedBy        = cmd.CustomerId
        };

        // Linked pickup_request so the auto-dispatcher (Wave 1.4) offers/assigns a rider,
        // tier-aware, and the rider task flow advances the parcel order.
        var prCount = await _db.PickupRequests.CountAsync(p => p.BrandId == brandId, ct);
        var pickup = new PickupRequest
        {
            Id                      = Guid.NewGuid(),
            RequestNumber           = $"PKP-{now.Year}-{brandId.ToString()[..4].ToUpper()}-{(prCount + 1):D6}",
            BrandId                 = brandId,
            FranchiseId             = store.FranchiseId,
            StoreId                 = store.Id,
            CustomerId              = cmd.CustomerId,
            AddressId               = req.PickupAddressId,
            PickupDate              = DateOnly.FromDateTime(now.UtcDateTime),
            PickupWindowStart       = TimeOnly.FromDateTime(now.UtcDateTime),
            PickupWindowEnd         = TimeOnly.FromDateTime(now.AddHours(2).UtcDateTime),
            IsExpress               = false,
            RequestedVehicleTier    = req.VehicleTier,
            ServicesRequested       = [],
            RequestedItems          = "[]",
            PaymentPreference       = req.PaymentPreference?.ToLowerInvariant() == "wallet" ? "wallet" : "cod",
            Source                  = "app",
            ConvertedOrderId        = order.Id,
            ConvertedOrderCreatedAt = order.CreatedAt,
            CustomerNotes           = req.NotesCustomer,
            Status                  = "pending",
            Metadata                = "{}",
            CreatedAt               = now,
            UpdatedAt               = now,
            CreatedBy               = cmd.CustomerId,
            UpdatedBy               = cmd.CustomerId
        };

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
                orderId = order.Id, orderNumber, brandId, jobType = JobType.Parcel,
                customerId = cmd.CustomerId, grandTotal, placedAt = now
            }),
            Metadata      = "{}",
            OccurredAt    = now,
            Status        = "pending",
            CreatedAt     = now,
            CreatedBy     = cmd.CustomerId
        };

        await _db.ExecuteInTransactionAsync(async innerCt =>
        {
            _db.Orders.Add(order);
            _db.OrderStatusHistories.Add(history);
            _db.PickupRequests.Add(pickup);
            _db.OutboxEvents.Add(outbox);
            await _db.SaveChangesAsync(innerCt);
        }, ct);

        return CreateOrderHandler.ToDto(order, [], [], [history]);
    }
}

public sealed class CreateParcelOrderValidator : AbstractValidator<CreateParcelOrderCommand>
{
    public CreateParcelOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.BrandId).NotEmpty();
        RuleFor(x => x.Request.PickupAddressId).NotEmpty();
        RuleFor(x => x.Request.DeliveryAddressId).NotEmpty()
            .NotEqual(x => x.Request.PickupAddressId)
            .WithMessage("Pickup and drop addresses must be different.");
        RuleFor(x => x.Request.FareQuoteToken).NotEmpty();
        RuleFor(x => x.Request.VehicleTier)
            .Must(t => t is null || VehicleTier.IsValid(t))
            .WithMessage($"VehicleTier must be one of: {string.Join(", ", VehicleTier.All)}.");
    }
}
