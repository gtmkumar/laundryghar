using System.Text.Json;
using laundryghar.Orders.Application.Common;
using laundryghar.Orders.Application.Orders.Dtos;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.Kernel;
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

        // ── Refund initiation (best-effort) ────────────────────────────────────
        // If the cancelled order has a captured/completed payment, create a
        // pending refund row and emit a refund.initiated outbox event.
        // Runs in the same SaveChangesAsync call for atomicity.
        // Does NOT call the gateway synchronously — the outbox worker handles that.
        await MaybeCreateRefundAsync(order, brandId, cmd.ActorId, now, ct);

        await _db.SaveChangesAsync(ct);
        return CreateOrderHandler.ToDto(order);
    }

    private async Task MaybeCreateRefundAsync(
        Order order, Guid brandId, Guid? actorId, DateTimeOffset now, CancellationToken ct)
    {
        // Look up any captured/completed payment for this order
        var payment = await _db.Payments
            .Where(p => p.OrderId == order.Id
                     && p.OrderCreatedAt == order.CreatedAt
                     && p.BrandId == brandId
                     && (p.Status == "captured" || p.Status == "completed"))
            .OrderByDescending(p => p.InitiatedAt)
            .FirstOrDefaultAsync(ct);

        if (payment is null) return;   // no qualifying payment — nothing to refund

        // Idempotency key derived from order id so retries produce the same row
        var idempotencyKey = $"cancel_refund_{order.Id:N}";

        // Guard: don't create a duplicate refund if already initiated
        var alreadyExists = await _db.PaymentRefunds
            .AnyAsync(r => r.IdempotencyKey == idempotencyKey, ct);

        if (alreadyExists) return;

        var refundNumber = $"REF-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30];

        var refund = new PaymentRefund
        {
            Id                = Guid.NewGuid(),
            BrandId           = brandId,
            OriginalPaymentId = payment.Id,
            OrderId           = order.Id,
            OrderCreatedAt    = order.CreatedAt,
            CustomerId        = payment.CustomerId,
            RefundNumber      = refundNumber,
            RefundType        = "gateway",
            Amount            = payment.Amount,
            Reason            = "order_cancelled",
            ReasonText        = order.CancellationReason,
            IdempotencyKey    = idempotencyKey,
            Status            = "pending",
            RequestedBy       = actorId,
            RequestedAt       = now,
            Metadata          = "{}",
            CreatedAt         = now,
            UpdatedAt         = now,
            CreatedBy         = actorId
        };

        _db.PaymentRefunds.Add(refund);

        // Outbox event — consumed by Worker to call the gateway
        var refundOutbox = new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "payment_refund",
            AggregateId   = refund.Id,
            EventType     = "refund.initiated",
            EventVersion  = 1,
            Payload       = System.Text.Json.JsonSerializer.Serialize(new
            {
                refundId          = refund.Id,
                originalPaymentId = payment.Id,
                orderId           = order.Id,
                brandId,
                amount            = refund.Amount,
                currencyCode      = payment.CurrencyCode,
                idempotencyKey,
                initiatedAt       = now
            }),
            Metadata   = "{}",
            OccurredAt = now,
            Status     = "pending",
            CreatedAt  = now,
            CreatedBy  = actorId
        };

        _db.OutboxEvents.Add(refundOutbox);
    }
}
