using core.Application.Common.Interfaces;
using core.Application.Engagement.Cms.MobileAppConfigs.Common;
using core.Application.Engagement.Cms.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Engagement.Cms.MobileAppConfigs.Commands.UpdateMobileAppConfig;

public sealed record UpdateMobileAppConfigCommand(Guid Id, UpdateMobileAppConfigRequest Request, Guid? ActorId) : ICommand<MobileAppConfigDto?>;

public class UpdateMobileAppConfigCommandHandler : ICommandHandler<UpdateMobileAppConfigCommand, MobileAppConfigDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateMobileAppConfigCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<MobileAppConfigDto?> HandleAsync(UpdateMobileAppConfigCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var entity = await _db.MobileAppConfigs
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.BrandId == brandId, cancellationToken);
        if (entity is null) return null;

        entity.ApplyFields(command.Request);
        entity.Status = command.Request.Status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return MobileAppConfigDto.FromEntity(entity);
    }
}
