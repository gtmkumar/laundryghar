using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.MobileAppConfigs.Common;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.EngagementCms;
using laundryghar.Utilities.Services;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Commands.CreateMobileAppConfig;

public sealed record CreateMobileAppConfigCommand(CreateMobileAppConfigRequest Request, Guid? ActorId) : ICommand<MobileAppConfigDto>;

public class CreateMobileAppConfigCommandHandler : ICommandHandler<CreateMobileAppConfigCommand, MobileAppConfigDto>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public CreateMobileAppConfigCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<MobileAppConfigDto> HandleAsync(CreateMobileAppConfigCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();

        var now = DateTimeOffset.UtcNow;
        var entity = new MobileAppConfig
        {
            Id = Guid.NewGuid(),
            BrandId = brandId,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = command.ActorId,
            UpdatedBy = command.ActorId,
        }.ApplyFields(command.Request);

        _db.MobileAppConfigs.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return MobileAppConfigDto.FromEntity(entity);
    }
}
