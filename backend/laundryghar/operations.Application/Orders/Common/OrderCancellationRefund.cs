using System.Text.Json;
using laundryghar.SharedDataModel.Entities.Commerce;
using laundryghar.SharedDataModel.Entities.Kernel;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Orders.Common;

/// <summary>
/// A0.8 — the single refund rule for a cancelled order.
///
/// <para>There are three ways an order reaches <c>cancelled</c>: an admin cancel
/// (<c>CancelOrderCommand</c>), a customer cancel (<c>CancelOrderByCustomerCommand</c>), and a
/// status update whose target happens to be cancelled (<c>UpdateOrderStatusCommand</c>). Only the
/// first one created a refund. The other two moved a paid order to cancelled and left the money
/// with the brand — silently, with no error and no trace, because nothing in the system says a
/// cancelled order must have a refund.</para>
///
/// <para>Which of the three a cancellation goes through is a routing detail: the customer app calls
/// one endpoint, the admin console calls another, and the POS status dropdown calls a third. A
/// customer who cancels from the app and one whose order an operator cancels for them are in
/// identical situations and must get identical treatment. So the rule lives here, once, and each
/// path calls it.</para>
///
/// <para>Idempotent by construction: the key is derived from the order id, so a retry, a
/// double-click or two paths racing produce one refund row rather than two.</para>
/// </summary>
public static class OrderCancellationRefund
{
    /// <summary>
    /// Queues a refund for any captured payment on <paramref name="order"/>. Adds to the change
    /// tracker only — the caller's own <c>SaveChangesAsync</c> commits it, so the refund and the
    /// cancellation land in one transaction or neither does.
    /// </summary>
    /// <returns>The refund that was queued, or null when there was nothing to refund.</returns>
    public static async Task<PaymentRefund?> QueueAsync(
        IOperationsDbContext db,
        Order order,
        Guid brandId,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var payment = await db.Payments
            .Where(p => p.OrderId == order.Id
                     && p.OrderCreatedAt == order.CreatedAt
                     && p.BrandId == brandId
                     && (p.Status == "captured" || p.Status == "completed"))
            .OrderByDescending(p => p.InitiatedAt)
            .FirstOrDefaultAsync(ct);

        if (payment is null) return null;   // nothing captured — nothing to give back

        var idempotencyKey = $"cancel_refund_{order.Id:N}";

        if (await db.PaymentRefunds.AnyAsync(r => r.IdempotencyKey == idempotencyKey, ct))
            return null;

        var refund = new PaymentRefund
        {
            Id                = Guid.NewGuid(),
            BrandId           = brandId,
            OriginalPaymentId = payment.Id,
            OrderId           = order.Id,
            OrderCreatedAt    = order.CreatedAt,
            CustomerId        = payment.CustomerId,
            RefundNumber      = $"REF-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30],
            RefundType        = "full",
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
            CreatedBy         = actorId,
        };

        db.PaymentRefunds.Add(refund);

        // The gateway call is the worker's job — doing it inline would put a third-party network
        // call inside the cancellation transaction.
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id            = Guid.NewGuid(),
            BrandId       = brandId,
            AggregateType = "payment_refund",
            AggregateId   = refund.Id,
            EventType     = "refund.initiated",
            EventVersion  = 1,
            Payload       = JsonSerializer.Serialize(new
            {
                refundId          = refund.Id,
                originalPaymentId = payment.Id,
                orderId           = order.Id,
                brandId,
                amount            = refund.Amount,
                currencyCode      = payment.CurrencyCode,
                idempotencyKey,
                initiatedAt       = now,
            }),
            Metadata   = "{}",
            OccurredAt = now,
            Status     = "pending",
            CreatedAt  = now,
            CreatedBy  = actorId,
        });

        return refund;
    }
}
