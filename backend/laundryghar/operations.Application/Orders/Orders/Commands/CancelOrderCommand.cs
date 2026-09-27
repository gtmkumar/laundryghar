using System.Text.Json;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Enums;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Fulfillment;
using operations.Application.Orders.Common;
using operations.Application.Orders.Orders.Dtos;

namespace operations.Application.Orders.Orders.Commands;

public sealed record CancelOrderCommand(Guid OrderId, string? Reason, bool IsCustomer, Guid? ActorId)
    : ICommand<OrderDto?>;

public sealed class CancelOrderHandler : ICommandHandler<CancelOrderCommand, OrderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFulfillmentStrategyResolver _strategies;

    public CancelOrderHandler(IOperationsDbContext db, ICurrentUser user, IFulfillmentStrategyResolver strategies)
    {
        _db   = db;
        _user = user;
        _strategies = strategies;
    }

    public async Task<OrderDto?> HandleAsync(CancelOrderCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId && o.BrandId == brandId, ct);
        if (order is null || order.DeletedAt != null) return null;

        if (!_user.IsWithinScope(brandId: order.BrandId, franchiseId: order.FranchiseId, storeId: order.StoreId, warehouseId: order.WarehouseId))
            throw new ForbiddenException("This order is outside your assigned scope.");

        var strategy = _strategies.ResolveForOrder(order);

        // Customer can only cancel if placed or pickup_scheduled
        if (cmd.IsCustomer && !strategy.CanCustomerCancel(order.Status))
            throw new BusinessRuleException(
                $"Customers may not cancel an order in status '{order.Status}'. " +
                $"Contact support for orders already picked up.");

        // Admin cancel uses the fulfilment-mode state machine
        if (!cmd.IsCustomer)
            strategy.EnsureTransition(order.Status, OrderStatus.Cancelled);

        var fromStatus = order.Status;
        order.Status           = OrderStatus.Cancelled;
        order.LifecycleState   = strategy.LifecycleStateFor(OrderStatus.Cancelled);
        order.CancelledAt      = now;
        order.CancellationReason = cmd.Reason;
        order.CancelledByType  = cmd.IsCustomer ? "customer" : "user";
        order.CancelledById    = cmd.ActorId;
        order.UpdatedAt        = now;
        order.UpdatedBy        = cmd.ActorId;
        order.Version++;

        var history = new OrderStatusHistory
        {
            Id               = Guid.NewGuid(),
            OrderId          = order.Id,
            OrderCreatedAt   = order.CreatedAt,
            BrandId          = brandId,
            FromStatus       = fromStatus,
            ToStatus         = OrderStatus.Cancelled,
            ChangedAt        = now,
            ChangedByType    = cmd.IsCustomer ? "customer" : "user",
            ChangedById      = cmd.ActorId,
            Reason           = cmd.Reason,
            CustomerNotified = cmd.IsCustomer,
            Metadata         = "{}",
            CreatedAt        = now,
            CreatedBy        = cmd.ActorId
        };

        var outbox = new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "order",
            AggregateId   = order.Id,
            EventType     = "order.status_changed",
            EventVersion  = 1,
            Payload       = JsonSerializer.Serialize(new
            {
                orderId     = order.Id,
                orderNumber = order.OrderNumber,
                brandId,
                fromStatus,
                toStatus    = OrderStatus.Cancelled,
                reason      = cmd.Reason,
                changedAt   = now,
                // ISO date (yyyy-MM-dd) when a pickup slot was booked; null otherwise.
                pickupDate  = order.PickupScheduledAt?.ToString("yyyy-MM-dd")
            }),
            Metadata    = "{}",
            OccurredAt  = now,
            Status      = "pending",
            CreatedAt   = now,
            CreatedBy   = cmd.ActorId
        };

        _db.OrderStatusHistories.Add(history);
        _db.OutboxEvents.Add(outbox);

        // ── Refund initiation ───────────────────────────────────────────────────
        // A0.8 — the rule now lives in OrderCancellationRefund and is called by all three
        // cancellation paths. It used to be a private method here, which is why the other two
        // cancelled paid orders without refunding them.
        await OrderCancellationRefund.QueueAsync(_db, order, brandId, cmd.ActorId, now, ct);

        await _db.SaveChangesAsync(ct);
        return CreateOrderHandler.ToDto(order);
    }
}

// ── Validators ────────────────────────────────────────────────────────────────

public sealed class CancelOrderValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => x.Reason is not null);
    }
}

public sealed class CancelOrderByCustomerValidator : AbstractValidator<CancelOrderByCustomerCommand>
{
    public CancelOrderByCustomerValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .When(x => x.Reason is not null);
    }
}
