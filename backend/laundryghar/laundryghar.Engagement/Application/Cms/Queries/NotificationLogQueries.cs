using laundryghar.Engagement.Application.Cms.Dtos;
using laundryghar.Utilities.Common;
using MediatR;

namespace laundryghar.Engagement.Application.Cms.Queries;

// ── Outbox ─────────────────────────────────────────────────────────────────────

public sealed record GetNotificationOutboxQuery(int Page, int PageSize, string? Status)
    : IRequest<PaginatedList<NotificationOutboxDto>>;

public sealed class GetNotificationOutboxHandler
    : IRequestHandler<GetNotificationOutboxQuery, PaginatedList<NotificationOutboxDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetNotificationOutboxHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PaginatedList<NotificationOutboxDto>> Handle(
        GetNotificationOutboxQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var q = _db.NotificationOutboxes.Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Status))
            q = q.Where(x => x.Status == query.Status);

        var projected = q.OrderByDescending(x => x.CreatedAt)
            .Select(x => new NotificationOutboxDto(
                x.Id, x.BrandId, x.TemplateId, x.TemplateCode, x.Channel, x.Locale,
                x.RecipientType, x.RecipientId,
                x.RecipientPhone, x.RecipientEmail,
                x.Body, x.Subject, x.Priority,
                x.ScheduledAt, x.ExpiresAt,
                x.Attempts, x.MaxAttempts,
                x.LastAttemptAt, x.LastError,
                x.SentAt, x.Provider, x.ProviderMessageId,
                x.Status, x.SuppressionReason,
                x.CreatedAt));

        return await PaginatedList<NotificationOutboxDto>.CreateAsync(projected, query.Page, query.PageSize, ct);
    }
}

// ── Notification Log ───────────────────────────────────────────────────────────

public sealed record GetNotificationLogsQuery(int Page, int PageSize, string? Channel)
    : IRequest<PaginatedList<NotificationLogDto>>;

public sealed class GetNotificationLogsHandler
    : IRequestHandler<GetNotificationLogsQuery, PaginatedList<NotificationLogDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetNotificationLogsHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PaginatedList<NotificationLogDto>> Handle(
        GetNotificationLogsQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var q = _db.NotificationLogs.Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Channel))
            q = q.Where(x => x.Channel == query.Channel);

        var projected = q.OrderByDescending(x => x.SentAt)
            .Select(x => new NotificationLogDto(
                x.Id, x.SentAt, x.BrandId, x.OutboxId,
                x.Channel, x.TemplateCode, x.RecipientType, x.RecipientId,
                x.RecipientAddress, x.Provider, x.ProviderMessageId,
                x.Status, x.DeliveredAt, x.ReadAt, x.ClickedAt,
                x.FailureCode, x.FailureMessage, x.Cost,
                x.ReferenceType, x.ReferenceId, x.CreatedAt));

        return await PaginatedList<NotificationLogDto>.CreateAsync(projected, query.Page, query.PageSize, ct);
    }
}

// ── WhatsApp Message Log ───────────────────────────────────────────────────────

public sealed record GetWhatsAppLogsQuery(int Page, int PageSize, string? Direction)
    : IRequest<PaginatedList<WhatsAppMessageLogDto>>;

public sealed class GetWhatsAppLogsHandler
    : IRequestHandler<GetWhatsAppLogsQuery, PaginatedList<WhatsAppMessageLogDto>>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public GetWhatsAppLogsHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<PaginatedList<WhatsAppMessageLogDto>> Handle(
        GetWhatsAppLogsQuery query, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var q = _db.WhatsAppMessageLogs.Where(x => x.BrandId == brandId);
        if (!string.IsNullOrEmpty(query.Direction))
            q = q.Where(x => x.Direction == query.Direction);

        var projected = q.OrderByDescending(x => x.SentAt)
            .Select(x => new WhatsAppMessageLogDto(
                x.Id, x.BrandId, x.Direction, x.CustomerId, x.UserId,
                x.PhoneE164, x.Provider, x.WaMessageId, x.WaConversationId,
                x.TemplateName, x.MessageType, x.BodyText,
                x.ReferenceType, x.ReferenceId, x.Status,
                x.SentAt, x.DeliveredAt, x.ReadAt,
                x.FailedAt, x.ErrorCode, x.ErrorMessage,
                x.CreatedAt));

        return await PaginatedList<WhatsAppMessageLogDto>.CreateAsync(projected, query.Page, query.PageSize, ct);
    }
}
