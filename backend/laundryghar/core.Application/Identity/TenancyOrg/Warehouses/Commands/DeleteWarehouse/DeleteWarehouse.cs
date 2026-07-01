using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;

namespace core.Application.Identity.TenancyOrg.Warehouses.Commands.DeleteWarehouse;

public sealed record DeleteWarehouseCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteWarehouseCommandHandler : ICommandHandler<DeleteWarehouseCommand, bool>
{
    private readonly ICoreDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteWarehouseCommandHandler(ICoreDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(DeleteWarehouseCommand command, CancellationToken cancellationToken)
    {
        var w = await _db.Warehouses.FindAsync([command.Id], cancellationToken);
        if (w is null) return false;
        if (!_user.IsWithinScope(brandId: w.BrandId, franchiseId: w.FranchiseId, warehouseId: w.Id))
            throw new ForbiddenException("This warehouse is outside your assigned scope.");
        w.DeletedAt = DateTimeOffset.UtcNow; w.UpdatedBy = command.ActorId; w.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
