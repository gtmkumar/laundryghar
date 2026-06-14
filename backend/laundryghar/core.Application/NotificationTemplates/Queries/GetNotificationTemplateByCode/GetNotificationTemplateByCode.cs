using core.Application.Repositories;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Results;

namespace core.Application.NotificationTemplates.Queries.GetNotificationTemplateByCode;

public record GetNotificationTemplateByCodeQuery(Guid BrandId, string Code) : IQuery<Result<NotificationTemplateDto>>;

public class GetNotificationTemplateByCodeQueryHandler
    : IQueryHandler<GetNotificationTemplateByCodeQuery, Result<NotificationTemplateDto>>
{
    private readonly INotificationTemplateRepository _repository;

    public GetNotificationTemplateByCodeQueryHandler(INotificationTemplateRepository repository)
        => _repository = repository;

    public async Task<Result<NotificationTemplateDto>> HandleAsync(GetNotificationTemplateByCodeQuery query, CancellationToken cancellationToken)
    {
        var result = await _repository.GetByCodeAsync(query.BrandId, query.Code, cancellationToken);

        return result.HasSuccess
            ? new Result<NotificationTemplateDto>(result.ResultCode, NotificationTemplateDto.FromEntity(result.UserObject!))
            : new Result<NotificationTemplateDto>(result.ResultCode);
    }
}
