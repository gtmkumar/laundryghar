namespace commerce.Infrastructure.Worker.Abstractions;

/// <summary>Represents a dispatch request for a single outbound notification channel.</summary>
public sealed record ChannelSendRequest(
    Guid    OutboxId,
    Guid    BrandId,
    string  Channel,
    string  RecipientType,
    Guid?   RecipientId,
    string? RecipientPhone,
    string? RecipientEmail,
    string? TemplateCode,
    string  Body,
    /// <summary>
    /// The aggregate type the notification relates to (e.g. "order", "pickup_request").
    /// Sourced from <c>notifications_outbox.reference_type</c> — used by push senders
    /// to embed a deep-link <c>type</c>/<c>id</c> in the Expo data payload.
    /// </summary>
    string? ReferenceType = null,
    /// <summary>The aggregate id for the deep-link, from <c>notifications_outbox.reference_id</c>.</summary>
    Guid?   ReferenceId   = null);

/// <summary>Result of a channel send — carries the provider name for audit logging.</summary>
public sealed record ChannelSendResult(
    string ProviderName,
    string? ProviderMessageId = null);

/// <summary>
/// Abstraction over the actual notification delivery transport (SMS, WhatsApp, email, push, …).
/// Implementations return a <see cref="ChannelSendResult"/> with the provider name so the
/// dispatcher can record the correct provider in the audit log.
/// </summary>
public interface IChannelSender
{
    Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken ct = default);
}
