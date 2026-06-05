using laundryghar.Engagement.Application.Notifications.Abstractions;

namespace laundryghar.Engagement.Application.Notifications.Handlers;

/// <summary>
/// Development stub: logs notification dispatch to the console/logger instead of calling a real provider.
/// Writes to notifications_outbox so the outbox processor can exercise the full pipeline.
/// </summary>
public sealed class DevNotificationSender : INotificationSender
{
    private readonly LaundryGharDbContext _db;
    private readonly ILogger<DevNotificationSender> _logger;

    public DevNotificationSender(LaundryGharDbContext db, ILogger<DevNotificationSender> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task SendAsync(NotificationDispatchRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DEV NotificationSender] brand={BrandId} template={TemplateCode} channel={Channel} recipient={RecipientType}:{RecipientId}",
            request.BrandId, request.TemplateCode, request.Channel,
            request.RecipientType, request.RecipientId);

        // Resolve template for body rendering
        var template = await _db.NotificationTemplates
            .Where(t => t.BrandId == request.BrandId && t.Code == request.TemplateCode && t.IsActive)
            .FirstOrDefaultAsync(ct);

        var body = template is not null
            ? RenderTemplate(template.BodyTemplate, request.Variables)
            : $"[{request.TemplateCode}] " + string.Join(", ", request.Variables.Select(kv => $"{kv.Key}={kv.Value}"));

        var now = DateTimeOffset.UtcNow;
        var outbox = new NotificationOutbox
        {
            Id            = Guid.NewGuid(),
            BrandId       = request.BrandId,
            TemplateId    = template?.Id,
            TemplateCode  = request.TemplateCode,
            Channel       = request.Channel,
            Locale        = "en",
            RecipientType = request.RecipientType,
            RecipientId   = request.RecipientId,
            RecipientPhone = request.RecipientPhone,
            RecipientEmail = request.RecipientEmail,
            Body          = body,
            VariablesResolved = System.Text.Json.JsonSerializer.Serialize(request.Variables),
            ReferenceType = request.ReferenceType,
            ReferenceId   = request.ReferenceId,
            Priority      = 5,
            ScheduledAt   = now,
            Attempts      = 0,
            MaxAttempts   = 3,
            // Mark immediately as sent in dev — no real provider
            Status        = "sent",
            SentAt        = now,
            Provider      = "dev-stub",
            CreatedAt     = now
        };
        _db.NotificationOutboxes.Add(outbox);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("[DEV NotificationSender] Queued outbox entry {OutboxId}.", outbox.Id);
    }

    private static string RenderTemplate(string template, Dictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
            template = template.Replace($"{{{{{key}}}}}", value, StringComparison.OrdinalIgnoreCase);
        return template;
    }
}
