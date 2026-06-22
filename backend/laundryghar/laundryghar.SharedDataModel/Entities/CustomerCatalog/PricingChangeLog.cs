namespace laundryghar.SharedDataModel.Entities.CustomerCatalog;

/// <summary>Pricing audit entry (customer_catalog.pricing_change_log): a before/after snapshot
/// of a pricing change (fabric multiplier, price-list item rate, add-on) for the Change history
/// tab and one-click Revert. Brand-scoped; immutable except the reverted_at/by stamp.</summary>
public class PricingChangeLog
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string TargetKind { get; set; } = null!; // 'fabric_type' | 'price_list_item' | 'add_on'
    public Guid TargetId { get; set; }
    public string Summary { get; set; } = null!;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevertedAt { get; set; }
    public Guid? RevertedBy { get; set; }
}
