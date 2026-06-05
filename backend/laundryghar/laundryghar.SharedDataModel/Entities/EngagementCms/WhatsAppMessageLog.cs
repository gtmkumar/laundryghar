using laundryghar.SharedDataModel.Entities.CustomerCatalog;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>Raw WhatsApp message send/receive log (engagement_cms.whatsapp_message_log).
/// Append-style — has created_at, updated_at, created_by, updated_by; no version, no deleted_at.</summary>
public class WhatsAppMessageLog
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Direction { get; set; } = null!;
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public string PhoneE164 { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string? WaMessageId { get; set; }
    public string? WaConversationId { get; set; }
    public string? TemplateName { get; set; }
    public string? MessageType { get; set; }
    public string? BodyText { get; set; }
    public string? MediaS3Key { get; set; }
    public string? MediaMimeType { get; set; }
    public string? ButtonPayload { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? CostUnits { get; set; }

    /// <summary>jsonb — raw provider webhook payload.</summary>
    public string? RawPayload { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public Customer? Customer { get; set; }
    public User? User { get; set; }
}
