using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Results;
using Microsoft.EntityFrameworkCore;

namespace core.Application.NotificationTemplates.Queries.GetNotificationTemplateByCode;

public record GetNotificationTemplateByCodeQuery(Guid BrandId, string Code) : IQuery<Result<NotificationTemplateDto>>;

public class GetNotificationTemplateByCodeQueryHandler
    : IQueryHandler<GetNotificationTemplateByCodeQuery, Result<NotificationTemplateDto>>
{
    private readonly ICoreDbContext _db;

    public GetNotificationTemplateByCodeQueryHandler(ICoreDbContext db) => _db = db;

    public async Task<Result<NotificationTemplateDto>> HandleAsync(GetNotificationTemplateByCodeQuery query, CancellationToken cancellationToken)
    {
        var entity = await _db.NotificationTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.BrandId == query.BrandId && t.Code == query.Code, cancellationToken);

        return entity is null
            ? new Result<NotificationTemplateDto>(new ResultCode(ResultType.Error, 0, "Notification template not found."))
            : new Result<NotificationTemplateDto>(new ResultCode(ResultType.Success), NotificationTemplateDto.FromEntity(entity));
    }
}
