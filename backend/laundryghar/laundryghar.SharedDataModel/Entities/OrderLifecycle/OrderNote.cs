using laundryghar.SharedDataModel.Common;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.OrderLifecycle;

/// <summary>Staff/system note on an order (order_lifecycle.order_notes).
/// FK to orders uses composite key. Has created_at, created_by, deleted_at — no updated_at, no version.</summary>
public class OrderNote : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Part of composite FK to orders(id, created_at).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Partition-key column carried on child for composite FK.</summary>
    public DateTimeOffset OrderCreatedAt { get; set; }

    public Guid BrandId { get; set; }
    public string NoteType { get; set; } = null!;
    public string Visibility { get; set; } = null!;
    public string AuthorType { get; set; } = null!;
    public Guid? AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public string NoteText { get; set; } = null!;
    public string Attachments { get; set; } = null!;
    public bool IsPinned { get; set; }
    public bool IsResolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Order Order { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
}
