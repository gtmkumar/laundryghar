using System.Text.Json;
using laundryghar.Orders.Application.Common;
using laundryghar.Orders.Application.Orders.Dtos;
using MediatR;

namespace laundryghar.Orders.Application.Orders.Commands;

public sealed record CancelOrderCommand(Guid OrderId, string? Reason, bool IsCustomer, Guid? ActorId)
    : IRequest<OrderDto?>;

public sealed class CancelOrderHandler : IRequestHandler<CancelOrderCommand, OrderDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CancelOrderHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<OrderDto?> Handle(CancelOrderCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId && o.BrandId == brandId, ct);
        if (order is null || order.DeletedAt != null) return null;

        // Customer can only cancel if placed or pickup_scheduled
        if (cmd.IsCustomer && !OrderStateMachine.CanCustomerCancel(order.Status))
            throw new BusinessRuleException(
                $"Customers may not cancel an order in status '{order.Status}'. " +
                $"Contact support for orders already picked up.");

        // Admin cancel uses state machine
        if (!cmd.IsCustomer)
            OrderStateMachine.ValidateTransition(order.Status, OrderStatus.Cancelled);

        var fromStatus = order.Status;
        order.Status           = OrderStatus.Cancelled;
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
                orderId    = order.Id,
                brandId,
                fromStatus,
                toStatus   = OrderStatus.Cancelled,
                reason     = cmd.Reason,
                changedAt  = now
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
