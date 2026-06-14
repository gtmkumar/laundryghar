using laundryghar.SharedDataModel.Entities.EngagementCms;

namespace core.Application.NotificationTemplates.Queries.GetNotificationTemplateByCode;

/// <summary>Read model for a notification template. Mapped from the entity by hand (no AutoMapper).</summary>
public sealed record NotificationTemplateDto
{
    public Guid Id { get; init; }
    public Guid BrandId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
    public bool IsActive { get; init; }

    public static NotificationTemplateDto FromEntity(NotificationTemplate e) => new()
    {
        Id = e.Id,
        BrandId = e.BrandId,
        Code = e.Code,
        Name = e.Name,
        Channel = e.Channel,
        Category = e.Category,
        Locale = e.Locale,
        IsActive = e.IsActive,
    };
}
