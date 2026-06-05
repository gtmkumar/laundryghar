using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>Transactional notification dispatch queue (engagement_cms.notifications_outbox).
/// Append-only log — has created_at, created_by only; no updated_at, no deleted_at.</summary>
public class NotificationOutbox
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public Guid? TemplateId { get; set; }
    public string TemplateCode { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public string Locale { get; set; } = null!;
    public string RecipientType { get; set; } = null!;
    public Guid? RecipientId { get; set; }
    public string? RecipientPhone { get; set; }

    /// <summary>citext column — mapped as string.</summary>
    public string? RecipientEmail { get; set; }

    public string? RecipientFcmToken { get; set; }
    public string? RecipientApnsToken { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = null!;

    /// <summary>jsonb — resolved variable values.</summary>
    public string? VariablesResolved { get; set; }

    public string? PushTitle { get; set; }
    public string? PushDeeplink { get; set; }

    /// <summary>jsonb — extra push payload.</summary>
    public string? PushPayload { get; set; }

    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public Guid? CorrelationId { get; set; }
    public short Priority { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public short Attempts { get; set; }
    public short MaxAttempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? Provider { get; set; }
    public string? ProviderMessageId { get; set; }
    public string Status { get; set; } = null!;
    public string? SuppressionReason { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Navigations
    public Brand Brand { get; set; } = null!;
    public NotificationTemplate? Template { get; set; }
    public ICollection<NotificationLog> NotificationLogs { get; set; } = [];
}
