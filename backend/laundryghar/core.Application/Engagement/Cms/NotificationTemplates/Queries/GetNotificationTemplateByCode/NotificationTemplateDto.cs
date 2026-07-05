using laundryghar.SharedDataModel.Entities.EngagementCms;

namespace core.Application.NotificationTemplates.Queries.GetNotificationTemplateByCode;

/// <summary>Read model for a notification template. Mapped from the entity by hand (no AutoMapper).
/// Shared by the list, by-code and by-id queries; the admin CMS UI reads <c>Status</c>,
/// <c>UpdatedAt</c>, <c>IsTransactional</c> in the table and the body/subject/variables fields
/// in the edit form (which is seeded from the list row), so this projection must be complete.</summary>
public sealed record NotificationTemplateDto
{
    public Guid Id { get; init; }
    public Guid BrandId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
    public string? SubjectTemplate { get; init; }
    public string BodyTemplate { get; init; } = string.Empty;
    public string? SmsSenderId { get; init; }
    public string? WhatsAppTemplateName { get; init; }
    public string? WhatsAppTemplateId { get; init; }
    public string? PushTitleTemplate { get; init; }
    public string? PushActionDeeplink { get; init; }
    public string Variables { get; init; } = "{}";
    public int VersionNumber { get; init; }
    public bool IsTransactional { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static NotificationTemplateDto FromEntity(NotificationTemplate e) => new()
    {
        Id = e.Id,
        BrandId = e.BrandId,
        Code = e.Code,
        Name = e.Name,
        Description = e.Description,
        Channel = e.Channel,
        Category = e.Category,
        Locale = e.Locale,
        SubjectTemplate = e.SubjectTemplate,
        BodyTemplate = e.BodyTemplate,
        SmsSenderId = e.SmsSenderId,
        WhatsAppTemplateName = e.WhatsAppTemplateName,
        WhatsAppTemplateId = e.WhatsAppTemplateId,
        PushTitleTemplate = e.PushTitleTemplate,
        PushActionDeeplink = e.PushActionDeeplink,
        Variables = e.Variables,
        VersionNumber = e.VersionNumber,
        IsTransactional = e.IsTransactional,
        IsActive = e.IsActive,
        ApprovedAt = e.ApprovedAt,
        Status = e.Status,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}
