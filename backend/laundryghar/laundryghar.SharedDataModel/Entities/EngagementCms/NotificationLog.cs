using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>Immutable sent-notification audit log (engagement_cms.notifications_log).
/// PARTITIONED table — composite PK (Id, SentAt) required by PG range partitioning on sent_at.
/// Append-only log — has created_at, created_by only; no updated_at, no deleted_at.</summary>
public class NotificationLog
{
    public Guid Id { get; set; }

    /// <summary>Partition key — part of composite PK.</summary>
    public DateTimeOffset SentAt { get; set; }

    public Guid BrandId { get; set; }
    public Guid? OutboxId { get; set; }
    public string Channel { get; set; } = null!;
    public string? TemplateCode { get; set; }
    public string RecipientType { get; set; } = null!;
    public Guid? RecipientId { get; set; }
    public string? RecipientAddress { get; set; }
    public string? Provider { get; set; }
    public string? ProviderMessageId { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? ClickedAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public decimal? Cost { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public NotificationOutbox? Outbox { get; set; }
}
