namespace laundryghar.Worker.Abstractions;

/// <summary>Represents a dispatch request for a single outbound notification channel.</summary>
public sealed record ChannelSendRequest(
    Guid   OutboxId,
    Guid   BrandId,
    string Channel,
    string RecipientType,
    Guid?  RecipientId,
    string? RecipientPhone,
    string? RecipientEmail,
    string? TemplateCode,
    string  Body);

/// <summary>
/// Abstraction over the actual notification delivery transport (SMS, WhatsApp, email, push, …).
/// Swap the <see cref="LoggingChannelSender"/> dev stub for a real provider implementation
/// (e.g. Twilio, Firebase, SendGrid) without touching the dispatcher logic.
/// </summary>
public interface IChannelSender
{
    Task SendAsync(ChannelSendRequest request, CancellationToken ct = default);
}
