using System.Net;
using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Payment record (commerce.payments).
/// Has created_at, updated_at, created_by, updated_by — NO version, NO deleted_at.
/// idempotency_key has a UNIQUE constraint.
/// payment_number has a UNIQUE constraint.
/// order_id + order_created_at → composite FK to order_lifecycle.orders(id, created_at) ON DELETE RESTRICT.
/// direction: 1 = inbound, -1 = outbound (smallint CHECK).</summary>
public class Payment
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? FranchiseId { get; set; }
    public Guid? StoreId { get; set; }
    public Guid? CustomerId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at).</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK.</summary>
    public DateTimeOffset? OrderCreatedAt { get; set; }

    public Guid? PaymentMethodId { get; set; }
    public string PaymentPurpose { get; set; } = null!;
    public string PaymentNumber { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal ConvenienceFee { get; set; }
    public decimal GatewayCharge { get; set; }
    public decimal NetAmount { get; set; }

    /// <summary>character(3) fixed-length currency code.</summary>
    public string CurrencyCode { get; set; } = null!;

    /// <summary>1 = inbound, -1 = outbound.</summary>
    public short Direction { get; set; }

    public string? Gateway { get; set; }
    public string? GatewayOrderId { get; set; }
    public string? GatewayPaymentId { get; set; }
    public string? GatewaySignature { get; set; }

    /// <summary>jsonb — raw gateway response payload.</summary>
    public string? GatewayResponse { get; set; }

    public string? UpiVpa { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardNetwork { get; set; }
    public string? BankName { get; set; }
    public string Status { get; set; } = null!;
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset InitiatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
    public string? SettlementId { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public decimal? SettledAmount { get; set; }

    /// <summary>Unique idempotency key — prevents duplicate payment creation.</summary>
    public string? IdempotencyKey { get; set; }

    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Notes { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Franchise? Franchise { get; set; }
    public Store? Store { get; set; }
    public Customer? Customer { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public ICollection<PaymentRefund> Refunds { get; set; } = [];
    public ICollection<CustomerPackage> CustomerPackages { get; set; } = [];
    public ICollection<WalletTransaction> WalletTransactions { get; set; } = [];
}
