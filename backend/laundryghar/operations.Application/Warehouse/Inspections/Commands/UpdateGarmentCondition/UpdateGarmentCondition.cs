using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Inspections.Commands.CreateGarmentCondition;
using operations.Application.Warehouse.Inspections.Dtos;

namespace operations.Application.Warehouse.Inspections.Commands.UpdateGarmentCondition;

public sealed record UpdateGarmentConditionCommand(
    Guid Id, UpdateGarmentConditionRequest Request, Guid? ActorId)
    : ICommand<GarmentConditionDto?>;

public sealed class UpdateGarmentConditionCommandHandler
    : ICommandHandler<UpdateGarmentConditionCommand, GarmentConditionDto?>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateGarmentConditionCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<GarmentConditionDto?> HandleAsync(UpdateGarmentConditionCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var e = await _db.FulfillmentUnitConditions
            .FirstOrDefaultAsync(c => c.Id == command.Id && c.BrandId == brandId, cancellationToken);
        if (e is null) return null;

        var req = command.Request;
        e.Name               = req.Name;
        e.NameLocalized      = req.NameLocalized;
        e.RequiresDisclaimer = req.RequiresDisclaimer;
        e.DisclaimerText     = req.DisclaimerText;
        e.DisplayOrder       = req.DisplayOrder;
        e.Status             = req.Status;
        e.IsActive           = req.Status == "active";
        e.UpdatedAt          = DateTimeOffset.UtcNow;
        e.UpdatedBy          = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return CreateGarmentConditionCommandHandler.ToDto(e);
    }
}
