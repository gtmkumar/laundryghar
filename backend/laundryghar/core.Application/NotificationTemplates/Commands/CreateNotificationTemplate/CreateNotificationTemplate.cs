using core.Application.Common.Interfaces;
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
    private readonly ICoreDbContext _db;

    public CreateNotificationTemplateCommandHandler(ICoreDbContext db) => _db = db;

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

        _db.NotificationTemplates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new Result<Guid>(new ResultCode(ResultType.Success, 1, "Template created."), entity.Id);
    }
}
