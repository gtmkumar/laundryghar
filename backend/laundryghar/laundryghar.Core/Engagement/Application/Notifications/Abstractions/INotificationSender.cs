namespace laundryghar.Engagement.Application.Notifications.Abstractions;

/// <summary>Dispatch request for a single notification.</summary>
public sealed record NotificationDispatchRequest(
    Guid BrandId,
    string TemplateCode,
    string Channel,
    string RecipientType,
    Guid? RecipientId,
    string? RecipientPhone,
    string? RecipientEmail,
    Dictionary<string, string> Variables,
    string? ReferenceType = null,
    Guid? ReferenceId = null);

/// <summary>Abstraction over notification dispatch — allows swapping stub with real provider.</summary>
public interface INotificationSender
{
    Task SendAsync(NotificationDispatchRequest request, CancellationToken ct = default);
}
