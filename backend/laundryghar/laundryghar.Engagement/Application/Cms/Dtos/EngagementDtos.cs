namespace laundryghar.Engagement.Application.Cms.Dtos;

// ── Notification Templates ─────────────────────────────────────────────────────

public sealed record NotificationTemplateDto(
    Guid Id, Guid BrandId, string Code, string Name, string? Description,
    string Channel, string Category, string Locale,
    string? SubjectTemplate, string BodyTemplate,
    string? SmsSenderId, string? WhatsAppTemplateName, string? WhatsAppTemplateId,
    string? PushTitleTemplate, string? PushActionDeeplink,
    string Variables, int VersionNumber, bool IsTransactional, bool IsActive,
    DateTimeOffset? ApprovedAt, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateNotificationTemplateRequest(
    string Code, string Name, string? Description,
    string Channel, string Category, string Locale,
    string? SubjectTemplate, string BodyTemplate,
    string? SmsSenderId, string? WhatsAppTemplateName, string? WhatsAppTemplateId,
    string? WhatsAppLangCode, string? WhatsAppNamespace,
    string? PushTitleTemplate, string? PushActionDeeplink, string? PushIconUrl, string? PushSound,
    string Variables, int VersionNumber, bool IsTransactional, bool IsActive);

public sealed record UpdateNotificationTemplateRequest(
    string Name, string? Description,
    string? SubjectTemplate, string BodyTemplate,
    string? SmsSenderId, string? WhatsAppTemplateName, string? WhatsAppTemplateId,
    string? WhatsAppLangCode, string? WhatsAppNamespace,
    string? PushTitleTemplate, string? PushActionDeeplink, string? PushIconUrl, string? PushSound,
    string Variables, bool IsTransactional, bool IsActive, string Status);

// ── Onboarding Slides ──────────────────────────────────────────────────────────

public sealed record OnboardingSlideDto(
    Guid Id, Guid BrandId, string AppType, string Title, string TitleLocalized,
    string? Description, string DescriptionLocalized,
    string ImageUrl, string? ImageDarkUrl, string? AnimationUrl,
    string? CtaText, string? CtaDeeplink,
    string? BackgroundColor, string? TextColor,
    short DisplayOrder, bool IsActive,
    DateTimeOffset? ShowFrom, DateTimeOffset? ShowUntil,
    string? MinAppVersion, string? MaxAppVersion,
    string[]? TargetSegments, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateOnboardingSlideRequest(
    string AppType, string Title, string TitleLocalized,
    string? Description, string DescriptionLocalized,
    string ImageUrl, string? ImageDarkUrl, string? AnimationUrl,
    string? CtaText, string? CtaDeeplink,
    string? BackgroundColor, string? TextColor,
    short DisplayOrder, bool IsActive,
    DateTimeOffset? ShowFrom, DateTimeOffset? ShowUntil,
    string? MinAppVersion, string? MaxAppVersion,
    string[]? TargetSegments);

public sealed record UpdateOnboardingSlideRequest(
    string AppType, string Title, string TitleLocalized,
    string? Description, string DescriptionLocalized,
    string ImageUrl, string? ImageDarkUrl, string? AnimationUrl,
    string? CtaText, string? CtaDeeplink,
    string? BackgroundColor, string? TextColor,
    short DisplayOrder, bool IsActive,
    DateTimeOffset? ShowFrom, DateTimeOffset? ShowUntil,
    string? MinAppVersion, string? MaxAppVersion,
    string[]? TargetSegments, string Status);

// ── App Banners ────────────────────────────────────────────────────────────────

public sealed record AppBannerDto(
    Guid Id, Guid BrandId, string AppType, string Placement,
    string? Title, string TitleLocalized,
    string? Subtitle, string SubtitleLocalized,
    string ImageUrl, string? ImageDarkUrl,
    string? CtaText, string? CtaDeeplink, string? ExternalUrl,
    Guid? PromotionId, Guid? CouponId, string? BackgroundColor,
    short DisplayOrder, bool IsActive,
    DateTimeOffset? ShowFrom, DateTimeOffset? ShowUntil,
    string? TargetAudience, string[]? TargetSegments, string[]? TargetCities,
    int ImpressionsCount, int ClicksCount, string? MinAppVersion,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateAppBannerRequest(
    string AppType, string Placement,
    string? Title, string TitleLocalized,
    string? Subtitle, string SubtitleLocalized,
    string ImageUrl, string? ImageDarkUrl,
    string? CtaText, string? CtaDeeplink, string? ExternalUrl,
    Guid? PromotionId, Guid? CouponId, string? BackgroundColor,
    short DisplayOrder, bool IsActive,
    DateTimeOffset? ShowFrom, DateTimeOffset? ShowUntil,
    string? TargetAudience, string[]? TargetSegments, string[]? TargetCities,
    string? MinAppVersion);

public sealed record UpdateAppBannerRequest(
    string AppType, string Placement,
    string? Title, string TitleLocalized,
    string? Subtitle, string SubtitleLocalized,
    string ImageUrl, string? ImageDarkUrl,
    string? CtaText, string? CtaDeeplink, string? ExternalUrl,
    Guid? PromotionId, Guid? CouponId, string? BackgroundColor,
    short DisplayOrder, bool IsActive,
    DateTimeOffset? ShowFrom, DateTimeOffset? ShowUntil,
    string? TargetAudience, string[]? TargetSegments, string[]? TargetCities,
    string? MinAppVersion, string Status);

// ── Mobile App Config ──────────────────────────────────────────────────────────

public sealed record MobileAppConfigDto(
    Guid Id, Guid BrandId, string AppType, string Platform,
    string ConfigKey, string ConfigValue,
    string? Description, bool IsForceUpdate,
    string? MinAppVersion, string? MaxAppVersion,
    string[]? TargetSegments, short? RolloutPercent,
    bool IsActive, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreateMobileAppConfigRequest(
    string AppType, string Platform, string ConfigKey, string ConfigValue,
    string? Description, bool IsForceUpdate,
    string? MinAppVersion, string? MaxAppVersion,
    string[]? TargetSegments, short? RolloutPercent, bool IsActive);

public sealed record UpdateMobileAppConfigRequest(
    string AppType, string Platform, string ConfigKey, string ConfigValue,
    string? Description, bool IsForceUpdate,
    string? MinAppVersion, string? MaxAppVersion,
    string[]? TargetSegments, short? RolloutPercent,
    bool IsActive, string Status);

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
