using core.Application.Common.Interfaces;
using core.Application.Identity.TenancyOrg.Dtos;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using LaundryGhar.Utilities.CQRS.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.TenancyOrg.Warehouses.Commands.UpdateWarehouse;

public sealed record UpdateWarehouseCommand(Guid Id, UpdateWarehouseRequest Request, Guid? ActorId) : ICommand<WarehouseDto?>;

public class UpdateWarehouseCommandHandler : ICommandHandler<UpdateWarehouseCommand, WarehouseDto?>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateWarehouseCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<WarehouseDto?> HandleAsync(UpdateWarehouseCommand command, CancellationToken cancellationToken)
    {
        var w = await _db.Warehouses.FindAsync([command.Id], cancellationToken);
        if (w is null) return null;
        if (!_user.IsWithinScope(brandId: w.BrandId, franchiseId: w.FranchiseId, warehouseId: w.Id))
            throw new ForbiddenException("This warehouse is outside your assigned scope.");
        if (command.Request.Name         is not null) w.Name         = command.Request.Name;
        if (command.Request.Status       is not null) w.Status       = command.Request.Status;
        if (command.Request.ContactPhone is not null) w.ContactPhone = command.Request.ContactPhone;
        w.UpdatedAt = DateTimeOffset.UtcNow; w.UpdatedBy = command.ActorId; w.Version++;
        await _db.SaveChangesAsync(cancellationToken);
        return new WarehouseDto(w.Id, w.BrandId, w.FranchiseId, w.Code, w.Name, w.City, w.Status, w.CreatedAt);
    }
}
