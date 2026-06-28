namespace laundryghar.SharedDataModel.Entities.IdentityAccess;

/// <summary>
/// A periodic invoice for a brand's platform subscription (identity_access.brand_platform_invoice).
/// Generated for the first period on tier-apply and for renewals by BrandPlatformBillingService.
/// Not auto-charged yet — status stays <c>issued</c> until a real gateway charger is wired
/// (the deferred P0 item); <c>Amount</c> is the tier price (tax handling is a follow-on).
/// </summary>
public class BrandPlatformInvoice
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid BrandId { get; set; }

    public DateTimeOffset BillingPeriodStart { get; set; }
    public DateTimeOffset BillingPeriodEnd { get; set; }

    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "INR";

    /// <summary><c>issued</c> | <c>paid</c> | <c>void</c>.</summary>
    public string Status { get; set; } = "issued";

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
