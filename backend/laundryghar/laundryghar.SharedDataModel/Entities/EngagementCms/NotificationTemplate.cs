using laundryghar.SharedDataModel.Entities.TenancyOrg;

namespace laundryghar.SharedDataModel.Entities.EngagementCms;

/// <summary>Notification content template per channel and locale (engagement_cms.notification_templates).
/// Has created_at, updated_at, created_by, updated_by — no version, no deleted_at.</summary>
public class NotificationTemplate
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Channel { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Locale { get; set; } = null!;
    public string? SubjectTemplate { get; set; }
    public string BodyTemplate { get; set; } = null!;
    public string? SmsSenderId { get; set; }
    public string? WhatsAppTemplateName { get; set; }
    public string? WhatsAppTemplateId { get; set; }
    public string? WhatsAppLangCode { get; set; }
    public string? WhatsAppNamespace { get; set; }
    public string? PushTitleTemplate { get; set; }
    public string? PushActionDeeplink { get; set; }
    public string? PushIconUrl { get; set; }
    public string? PushSound { get; set; }

    /// <summary>jsonb — variable descriptor array.</summary>
    public string Variables { get; set; } = null!;

    public int VersionNumber { get; set; }
    public Guid? ParentTemplateId { get; set; }
    public bool IsTransactional { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Status { get; set; } = null!;

    // Navigations
    public Brand Brand { get; set; } = null!;
    public NotificationTemplate? ParentTemplate { get; set; }
    public ICollection<NotificationTemplate> ChildTemplates { get; set; } = [];
    public ICollection<NotificationOutbox> NotificationOutboxes { get; set; } = [];
}
