using System.Text.Json;
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

        // Enforce state machine
        OrderStateMachine.ValidateTransition(order.Status, req.ToStatus);

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
                orderId    = order.Id,
                brandId,
                fromStatus,
                toStatus   = req.ToStatus,
                changedAt  = now,
                changedById = cmd.ActorId
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
