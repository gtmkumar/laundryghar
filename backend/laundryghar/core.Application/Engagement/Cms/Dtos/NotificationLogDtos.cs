namespace core.Application.Engagement.Cms.Dtos;

// ── Notification Outbox ────────────────────────────────────────────────────────

public sealed record NotificationOutboxDto(
    Guid Id, Guid BrandId, Guid? TemplateId, string TemplateCode, string Channel, string Locale,
    string RecipientType, Guid? RecipientId,
    string? RecipientPhone, string? RecipientEmail,
    string Body, string? Subject, short Priority,
    DateTimeOffset ScheduledAt, DateTimeOffset? ExpiresAt,
    short Attempts, short MaxAttempts,
    DateTimeOffset? LastAttemptAt, string? LastError,
    DateTimeOffset? SentAt, string? Provider, string? ProviderMessageId,
    string Status, string? SuppressionReason,
    DateTimeOffset CreatedAt);

// ── Notification Log ───────────────────────────────────────────────────────────

public sealed record NotificationLogDto(
    Guid Id, DateTimeOffset SentAt, Guid BrandId, Guid? OutboxId,
    string Channel, string? TemplateCode, string RecipientType, Guid? RecipientId,
    string? RecipientAddress, string? Provider, string? ProviderMessageId,
    string Status, DateTimeOffset? DeliveredAt, DateTimeOffset? ReadAt, DateTimeOffset? ClickedAt,
    string? FailureCode, string? FailureMessage, decimal? Cost,
    string? ReferenceType, Guid? ReferenceId, DateTimeOffset CreatedAt);

// ── WhatsApp Message Log ───────────────────────────────────────────────────────

public sealed record WhatsAppMessageLogDto(
    Guid Id, Guid BrandId, string Direction, Guid? CustomerId, Guid? UserId,
    string PhoneE164, string Provider, string? WaMessageId, string? WaConversationId,
    string? TemplateName, string? MessageType, string? BodyText,
    string? ReferenceType, Guid? ReferenceId, string? Status,
    DateTimeOffset SentAt, DateTimeOffset? DeliveredAt, DateTimeOffset? ReadAt,
    DateTimeOffset? FailedAt, string? ErrorCode, string? ErrorMessage,
    DateTimeOffset CreatedAt);
