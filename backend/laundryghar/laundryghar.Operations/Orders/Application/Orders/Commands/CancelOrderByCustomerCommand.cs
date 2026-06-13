using laundryghar.Orders.Infrastructure.Auth;
using laundryghar.Orders.Infrastructure.Services;
using System.Text.Json;
using laundryghar.Orders.Application.Common;
using laundryghar.Orders.Application.Orders.Dtos;
using MediatR;

namespace laundryghar.Orders.Application.Orders.Commands;

/// <summary>
/// Customer-initiated cancel. BrandId comes from the JWT brand_id claim (passed by endpoint).
/// Self-filter: only the customer who owns the order can cancel it.
/// </summary>
public sealed record CancelOrderByCustomerCommand(
    Guid OrderId, Guid CustomerId, Guid BrandId, string? Reason)
    : IRequest<OrderDto?>;

public sealed class CancelOrderByCustomerHandler
    : IRequestHandler<CancelOrderByCustomerCommand, OrderDto?>
{
    private readonly LaundryGharDbContext _db;

    public CancelOrderByCustomerHandler(LaundryGharDbContext db) => _db = db;

    public async Task<OrderDto?> Handle(CancelOrderByCustomerCommand cmd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Self-filter + brand predicate: customer_id AND brand_id from token
        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == cmd.OrderId
                                   && o.CustomerId == cmd.CustomerId
                                   && o.BrandId    == cmd.BrandId, ct);
        if (order is null || order.DeletedAt != null) return null;

        if (!OrderStateMachine.CanCustomerCancel(order.Status))
            throw new BusinessRuleException(
                $"You cannot cancel an order in status '{order.Status}'. " +
                "Contact support for orders that have already been picked up.");

        var fromStatus = order.Status;
        order.Status           = OrderStatus.Cancelled;
        order.CancelledAt      = now;
        order.CancellationReason = cmd.Reason;
        order.CancelledByType  = "customer";
        order.CancelledById    = cmd.CustomerId;
        order.UpdatedAt        = now;
        order.UpdatedBy        = cmd.CustomerId;
        order.Version++;

        var history = new OrderStatusHistory
        {
            Id               = Guid.NewGuid(),
            OrderId          = order.Id,
            OrderCreatedAt   = order.CreatedAt,
            BrandId          = cmd.BrandId,
            FromStatus       = fromStatus,
            ToStatus         = OrderStatus.Cancelled,
            ChangedAt        = now,
            ChangedByType    = "customer",
            ChangedById      = cmd.CustomerId,
            Reason           = cmd.Reason,
            CustomerNotified = true,
            Metadata         = "{}",
            CreatedAt        = now,
            CreatedBy        = cmd.CustomerId
        };

        var outbox = new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = cmd.BrandId,
            AggregateType = "order",
            AggregateId   = order.Id,
            EventType     = "order.status_changed",
            EventVersion  = 1,
            Payload       = JsonSerializer.Serialize(new
            {
                orderId    = order.Id,
                brandId    = cmd.BrandId,
                fromStatus,
                toStatus   = OrderStatus.Cancelled,
                changedAt  = now,
                initiatedBy = "customer"
            }),
            Metadata    = "{}",
            OccurredAt  = now,
            Status      = "pending",
            CreatedAt   = now,
            CreatedBy   = cmd.CustomerId
        };

        _db.OrderStatusHistories.Add(history);
        _db.OutboxEvents.Add(outbox);
        await _db.SaveChangesAsync(ct);
        return CreateOrderHandler.ToDto(order);
    }
}
