using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.NotificationLogs.Queries.GetNotificationLogs;

// Admin list (paged), scoped to the caller's brand. Read-only notification delivery log.
public sealed record GetNotificationLogsQuery(int Page, int PageSize, string? Channel)
    : IQuery<PaginatedList<NotificationLogDto>>;

public class GetNotificationLogsQueryHandler
    : IQueryHandler<GetNotificationLogsQuery, PaginatedList<NotificationLogDto>>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public GetNotificationLogsQueryHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<PaginatedList<NotificationLogDto>> HandleAsync(
        GetNotificationLogsQuery query, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var q = _db.NotificationLogs.AsNoTracking().Where(x => x.BrandId == brandId);
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

        return await PaginatedList<NotificationLogDto>.CreateAsync(
            projected, query.Page, query.PageSize, cancellationToken);
    }
}
