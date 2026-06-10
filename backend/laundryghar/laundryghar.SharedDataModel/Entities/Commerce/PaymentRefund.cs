using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Refund against a payment (commerce.payment_refunds).
/// Has created_at, updated_at, created_by — NO updated_by, NO version, NO deleted_at.
/// refund_number has a UNIQUE constraint.
/// order_id + order_created_at → composite FK to order_lifecycle.orders(id, created_at) ON DELETE RESTRICT.</summary>
public class PaymentRefund
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid OriginalPaymentId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public Guid? CustomerId { get; set; }
    public string RefundNumber { get; set; } = null!;
    public string RefundType { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = null!;
    public string? ReasonText { get; set; }
    public string? RefundMethod { get; set; }
    public string? GatewayRefundId { get; set; }

    /// <summary>jsonb — raw gateway response payload.</summary>
    public string? GatewayResponse { get; set; }

    public string Status { get; set; } = null!;
    public Guid? RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset? CustomerNotifiedAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Optional idempotency key — prevents duplicate refund creation on retries.
    /// Unique where not null (partial unique index).
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Payment OriginalPayment { get; set; } = null!;
    public Customer? Customer { get; set; }
    public ICollection<WalletTransaction> WalletTransactions { get; set; } = [];
}
