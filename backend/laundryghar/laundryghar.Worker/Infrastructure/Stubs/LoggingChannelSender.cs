using laundryghar.Worker.Abstractions;

namespace laundryghar.Worker.Infrastructure.Stubs;

/// <summary>
/// Development stub: logs notification dispatch instead of calling a real provider.
/// Replace with a real implementation (Twilio, Firebase, SendGrid, etc.) for production.
/// </summary>
internal sealed class LoggingChannelSender : IChannelSender
{
    private readonly ILogger<LoggingChannelSender> _logger;

    public LoggingChannelSender(ILogger<LoggingChannelSender> logger)
        => _logger = logger;

    public Task SendAsync(ChannelSendRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[NOTIFY] channel={Channel} recipient={RecipientType}:{RecipientId} " +
            "phone={Phone} email={Email} template={TemplateCode} outboxId={OutboxId}",
            request.Channel,
            request.RecipientType,
            request.RecipientId,
            request.RecipientPhone,
            request.RecipientEmail,
            request.TemplateCode,
            request.OutboxId);

        return Task.CompletedTask;
    }
}
