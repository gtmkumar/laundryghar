using laundryghar.SharedDataModel.Entities.CustomerCatalog;

namespace laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;

/// <summary>Customer recurring-payment authorization (UPI AutoPay / e-mandate / NACH)
/// (commerce.payment_mandates). No soft-delete — mandates are revoked, not deleted.</summary>
public class PaymentMandate
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerId { get; set; }
    public string MandateType { get; set; } = null!;
    public string Gateway { get; set; } = null!;
    public string? GatewayMandateId { get; set; }
    public string? GatewayToken { get; set; }
    public string? GatewayCustomerId { get; set; }
    public decimal MaxAmount { get; set; }
    public string DebitFrequency { get; set; } = null!;
    public string? UpiVpa { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardNetwork { get; set; }
    public string? BankName { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public DateTimeOffset? AuthenticatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    /// <summary>jsonb — raw gateway response.</summary>
    public string? GatewayResponse { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigations
    public Customer Customer { get; set; } = null!;
    public ICollection<CustomerSubscription> CustomerSubscriptions { get; set; } = [];
}
