using laundryghar.SharedDataModel.Entities.TenancyOrg;
using NetTopologySuite.Geometries;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Immutable audit trail of order status transitions (order_lifecycle.order_status_history).
/// FK to orders uses composite key. Has created_at, created_by only.</summary>
public class OrderStatusHistory
{
    public Guid Id { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Partition-key column carried on child for composite FK.</summary>
    public DateTimeOffset OrderCreatedAt { get; set; }

    public Guid BrandId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = null!;
    public string? FromSubStatus { get; set; }
    public string? ToSubStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string ChangedByType { get; set; } = null!;
    public Guid? ChangedById { get; set; }
    public string? ChangedByName { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public bool CustomerNotified { get; set; }
    public string[]? NotificationChannels { get; set; }

    /// <summary>GEOGRAPHY(Point,4326) — real geography type confirmed via \d+.</summary>
    public Point? Location { get; set; }

    public string Metadata { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Order Order { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
}
