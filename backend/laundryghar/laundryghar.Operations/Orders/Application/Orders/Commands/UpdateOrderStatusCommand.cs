using laundryghar.Orders.Infrastructure.Auth;
using laundryghar.Orders.Infrastructure.Services;
using System.Text.Json;
using FluentValidation;
using laundryghar.Orders.Application.Common;
using laundryghar.Orders.Application.Orders.Dtos;
using MediatR;

namespace laundryghar.Orders.Application.Orders.Commands;

public sealed record UpdateOrderStatusCommand(
    Guid OrderId,
    UpdateOrderStatusRequest Request,
    Guid? ActorId
) : IRequest<OrderDto?>;

public sealed class UpdateOrderStatusHandler : IRequestHandler<UpdateOrderStatusCommand, OrderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateOrderStatusHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<OrderDto?> Handle(UpdateOrderStatusCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Brand predicate: defense-in-depth on top of RLS
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId && o.BrandId == brandId, ct);
        if (order is null || order.DeletedAt != null) return null;

        // Enforce state machine (parcel jobs use a shorter point-to-point path)
        OrderStateMachine.ValidateTransition(order.Status, req.ToStatus, order.JobType);

        var fromStatus = order.Status;
        order.Status    = req.ToStatus;
        order.UpdatedAt = now;
        order.UpdatedBy = cmd.ActorId;
        order.Version++;

        // Update relevant timestamp columns
        switch (req.ToStatus)
        {
            case OrderStatus.PickupScheduled:   order.PickupScheduledAt  = now; break;
            case OrderStatus.PickedUp:          order.PickedUpAt         = now; break;
            case OrderStatus.Received:          order.ReceivedAt         = now; break;
            case OrderStatus.Qc:                /* qc_completed_at set when leaving Qc */ break;
            case OrderStatus.Ready:             order.ReadyAt            = now;
                                                order.QcCompletedAt      = now; break;
            case OrderStatus.OutForDelivery:    order.OutForDeliveryAt   = now; break;
            case OrderStatus.Delivered:         order.DeliveredAt        = now; break;
            case OrderStatus.Cancelled:
                order.CancelledAt        = now;
                order.CancellationReason = req.Reason;
                order.CancelledByType    = "user";
                order.CancelledById      = cmd.ActorId;
                break;
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
    // All valid order status values — mirrors OrderStatus constants and DB CHECK constraint.
    // Case-sensitive: DB stores lowercase; upper-cased values are rejected by design.
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
    {
        OrderStatus.Placed, OrderStatus.PickupScheduled, OrderStatus.PickupAssigned,
        OrderStatus.PickupInProgress, OrderStatus.PickedUp, OrderStatus.Received,
        OrderStatus.Sorting, OrderStatus.InProcess, OrderStatus.Qc, OrderStatus.Ready,
        OrderStatus.DeliveryScheduled, OrderStatus.DeliveryAssigned, OrderStatus.OutForDelivery,
        OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Returned,
        OrderStatus.Rewash, OrderStatus.Disputed, OrderStatus.Closed
    };

    public UpdateOrderStatusValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Request.ToStatus)
            .NotEmpty()
            .Must(s => ValidStatuses.Contains(s))
            .WithMessage($"ToStatus is not a recognised order status value.");
        RuleFor(x => x.Request.Reason)
            .MaximumLength(500)
            .When(x => x.Request.Reason is not null);
        RuleFor(x => x.Request.Notes)
            .MaximumLength(1000)
            .When(x => x.Request.Notes is not null);
    }
}
