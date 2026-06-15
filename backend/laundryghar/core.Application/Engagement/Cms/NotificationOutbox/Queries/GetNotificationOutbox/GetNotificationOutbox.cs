using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.NotificationOutbox.Queries.GetNotificationOutbox;

// Admin list (paged), scoped to the caller's brand. Read-only outbox log.
public sealed record GetNotificationOutboxQuery(int Page, int PageSize, string? Status)
    : IQuery<PaginatedList<NotificationOutboxDto>>;

public class GetNotificationOutboxQueryHandler
    : IQueryHandler<GetNotificationOutboxQuery, PaginatedList<NotificationOutboxDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetNotificationOutboxQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PaginatedList<NotificationOutboxDto>> HandleAsync(
        GetNotificationOutboxQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var q = _db.NotificationOutboxes.AsNoTracking().Where(x => x.BrandId == brandId);
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

        return await PaginatedList<NotificationOutboxDto>.CreateAsync(
            projected, query.Page, query.PageSize, cancellationToken);
    }
}
