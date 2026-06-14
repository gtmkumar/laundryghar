using core.Application.Repositories;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Results;

namespace core.Application.NotificationTemplates.Commands.CreateNotificationTemplate;

public record CreateNotificationTemplateCommand : ICommand<Result<Guid>>
{
    public Guid BrandId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
    public string BodyTemplate { get; init; } = string.Empty;
}

public class CreateNotificationTemplateCommandHandler
    : ICommandHandler<CreateNotificationTemplateCommand, Result<Guid>>
{
    private readonly INotificationTemplateRepository _repository;

    public CreateNotificationTemplateCommandHandler(INotificationTemplateRepository repository)
        => _repository = repository;

    public async Task<Result<Guid>> HandleAsync(CreateNotificationTemplateCommand command, CancellationToken cancellationToken)
    {
        var entity = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            BrandId = command.BrandId,
            Code = command.Code,
            Name = command.Name,
            Channel = command.Channel,
            Category = command.Category,
            Locale = command.Locale,
            BodyTemplate = command.BodyTemplate,
            Variables = "[]",
            IsActive = true,
        };

        var result = await _repository.AddAsync(entity, cancellationToken);
        return new Result<Guid>(result.ResultCode, entity.Id);
    }
}
