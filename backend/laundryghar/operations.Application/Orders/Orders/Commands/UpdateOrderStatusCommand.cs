using System.Text.Json;
using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
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

public sealed record UpdateOrderStatusCommand(
    Guid OrderId,
    UpdateOrderStatusRequest Request,
    Guid? ActorId
) : ICommand<OrderDto?>;

public sealed class UpdateOrderStatusHandler : ICommandHandler<UpdateOrderStatusCommand, OrderDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFulfillmentStrategyResolver _strategies;

    public UpdateOrderStatusHandler(IOperationsDbContext db, ICurrentUser user, IFulfillmentStrategyResolver strategies)
    {
        _db   = db;
        _user = user;
        _strategies = strategies;
    }

    public async Task<OrderDto?> HandleAsync(UpdateOrderStatusCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Brand predicate: defense-in-depth on top of RLS
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId && o.BrandId == brandId, ct);
        if (order is null || order.DeletedAt != null) return null;

        // Enforce the fulfilment-mode state machine (point_to_point uses a shorter path).
        // The strategy owns its status vocabulary — there is no shared closed list to validate
        // against (that would re-impose the laundry vocabulary the OrderStatus widening removed).
        var strategy = _strategies.ResolveForOrder(order);
        if (!strategy.IsKnownStatus(req.ToStatus))
            throw new BusinessRuleException(
                $"'{req.ToStatus}' is not a recognised status for fulfilment mode '{strategy.FulfillmentMode}'.");
        strategy.EnsureTransition(order.Status, req.ToStatus);

        var fromStatus = order.Status;
        order.Status         = req.ToStatus;
        order.LifecycleState = strategy.LifecycleStateFor(req.ToStatus);
        order.UpdatedAt = now;
        order.UpdatedBy = cmd.ActorId;
        order.Version++;

        // Update relevant timestamp columns — the per-status side-effects are owned by the
        // fulfilment strategy (vertical-neutral; no-op for statuses without a timestamp).
        strategy.ApplyTransitionEffects(order, req.ToStatus, now);

        // Cancellation carries command-specific metadata beyond the timestamp.
        if (req.ToStatus == OrderStatus.Cancelled)
        {
            order.CancelledAt        = now;
            order.CancellationReason = req.Reason;
            order.CancelledByType    = "user";
            order.CancelledById      = cmd.ActorId;
        }

        // Status history (immutable audit row)
        var history = new OrderStatusHistory
        {
            Id               = Guid.NewGuid(),
            OrderId          = order.Id,
            OrderCreatedAt   = order.CreatedAt,
            BrandId          = brandId,
            FromStatus       = fromStatus,
            ToStatus         = req.ToStatus,
            ChangedAt        = now,
            ChangedByType    = "user",
            ChangedById      = cmd.ActorId,
            Reason           = req.Reason,
            Notes            = req.Notes,
            CustomerNotified = req.CustomerNotified,
            Metadata         = "{}",
            CreatedAt        = now,
            CreatedBy        = cmd.ActorId
        };

        // Determine if this status change happens after the promised delivery date.
        // tatBreached = true signals downstream handlers (notifications, dashboards)
        // that the order is overdue. Computed once here — no sweeping job needed.
        var tatBreached = order.PromisedDeliveryAt.HasValue
            && now > order.PromisedDeliveryAt.Value
            && req.ToStatus is not (
                OrderStatus.Delivered or
                OrderStatus.Cancelled or
                OrderStatus.Closed or
                OrderStatus.Returned);

        // Outbox event
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
                orderId            = order.Id,
                orderNumber        = order.OrderNumber,
                brandId,
                fromStatus,
                toStatus           = req.ToStatus,
                changedAt          = now,
                changedById        = cmd.ActorId,
                // ISO date (yyyy-MM-dd) when a pickup slot is already booked; null otherwise.
                pickupDate         = order.PickupScheduledAt?.ToString("yyyy-MM-dd"),
                // True when this status change occurs after the promised delivery date;
                // notification mapping layer may use this to trigger an apology or escalation.
                tatBreached        = tatBreached,
                promisedDeliveryAt = order.PromisedDeliveryAt
            }),
            Metadata    = "{}",
            OccurredAt  = now,
            Status      = "pending",
            CreatedAt   = now,
            CreatedBy   = cmd.ActorId
        };

        _db.OrderStatusHistories.Add(history);
        _db.OutboxEvents.Add(outbox);
        await _db.SaveChangesAsync(ct);

        return CreateOrderHandler.ToDto(order);
    }
}

// ── Validator ─────────────────────────────────────────────────────────────────

public sealed class UpdateOrderStatusValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    // Structural validation only. Whether ToStatus is a recognised status is owned by the
    // order's fulfilment strategy (resolved per-order in the handler), NOT a shared closed list —
    // a static vocabulary here would re-impose the laundry statuses the OrderStatus widening
    // (Phase 1 slice B) deliberately removed from the DB CHECK.
    public UpdateOrderStatusValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Request.ToStatus)
            .NotEmpty();
        RuleFor(x => x.Request.Reason)
            .MaximumLength(500)
            .When(x => x.Request.Reason is not null);
        RuleFor(x => x.Request.Notes)
            .MaximumLength(1000)
            .When(x => x.Request.Notes is not null);
    }
}
