using core.Application.Common.Interfaces;
using core.Application.NotificationTemplates.Queries.GetNotificationTemplateByCode;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Common;
using Microsoft.EntityFrameworkCore;

namespace core.Application.NotificationTemplates.Queries.GetNotificationTemplates;

/// <summary>Admin list (paged) of notification templates, scoped to the caller's brand.
/// RLS also enforces brand isolation at the DB layer; the explicit filter keeps the query
/// intent obvious and mirrors <c>GetNotificationTemplateByCode</c>.</summary>
public sealed record GetNotificationTemplatesQuery(Guid BrandId, int Page, int PageSize)
    : IQuery<PaginatedList<NotificationTemplateDto>>;

public class GetNotificationTemplatesQueryHandler
    : IQueryHandler<GetNotificationTemplatesQuery, PaginatedList<NotificationTemplateDto>>
{
    private readonly ICoreDbContext _db;

    public GetNotificationTemplatesQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<PaginatedList<NotificationTemplateDto>> HandleAsync(
        GetNotificationTemplatesQuery query, CancellationToken cancellationToken)
    {
        var q = _db.NotificationTemplates.AsNoTracking()
            .Where(x => x.BrandId == query.BrandId)
            .OrderBy(x => x.Code);

        var paged = await PaginatedList<NotificationTemplate>.CreateAsync(
            q, query.Page, query.PageSize, cancellationToken);

        return paged.Map(NotificationTemplateDto.FromEntity);
    }
}
