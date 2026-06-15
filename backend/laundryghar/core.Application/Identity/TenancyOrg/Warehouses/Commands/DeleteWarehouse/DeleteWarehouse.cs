using core.Application.Common.Interfaces;
using LaundryGhar.Utilities.CQRS.Abstractions;

namespace core.Application.Identity.TenancyOrg.Warehouses.Commands.DeleteWarehouse;

public sealed record DeleteWarehouseCommand(Guid Id, Guid? ActorId) : ICommand<bool>;

public class DeleteWarehouseCommandHandler : ICommandHandler<DeleteWarehouseCommand, bool>
{
    private readonly ICoreDbContext _db;

    public DeleteWarehouseCommandHandler(ICoreDbContext db) => _db = db;

    public async Task<bool> HandleAsync(DeleteWarehouseCommand command, CancellationToken cancellationToken)
    {
        var w = await _db.Warehouses.FindAsync([command.Id], cancellationToken);
        if (w is null) return false;
        w.DeletedAt = DateTimeOffset.UtcNow; w.UpdatedBy = command.ActorId; w.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
