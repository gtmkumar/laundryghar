using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.Commerce;

/// <summary>Append-only record of a coupon redemption against an order (commerce.coupon_redemptions).
/// Has created_at, created_by ONLY — no updated_at, no version, no deleted_at.
/// order_id + order_created_at → composite FK to order_lifecycle.orders(id, created_at) ON DELETE RESTRICT.</summary>
public class CouponRedemption
{
    public Guid Id { get; set; }
    public Guid CouponId { get; set; }
    public Guid BrandId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>Part of composite FK to order_lifecycle.orders(id, created_at). Required.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Partition-key column carried for composite FK. Required.</summary>
    public DateTimeOffset OrderCreatedAt { get; set; }

    public string CouponCode { get; set; } = null!;
    public decimal DiscountAmount { get; set; }
    public decimal OrderSubtotalSnapshot { get; set; }
    public DateTimeOffset RedeemedAt { get; set; }
    public DateTimeOffset? RevertedAt { get; set; }
    public string? RevertedReason { get; set; }

    /// <summary>jsonb — arbitrary metadata.</summary>
    public string Metadata { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Coupon Coupon { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
}
