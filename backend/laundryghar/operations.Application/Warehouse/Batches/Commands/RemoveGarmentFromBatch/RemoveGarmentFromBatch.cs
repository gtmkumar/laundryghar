using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Warehouse.Batches.Commands.RemoveGarmentFromBatch;

// ── Remove garment from batch ─────────────────────────────────────────────────

public sealed record RemoveGarmentFromBatchCommand(Guid BatchId, Guid FulfillmentUnitId, Guid? ActorId)
    : ICommand<bool>;

public sealed class RemoveGarmentFromBatchCommandHandler : ICommandHandler<RemoveGarmentFromBatchCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public RemoveGarmentFromBatchCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(RemoveGarmentFromBatchCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;

        var garment = await _db.FulfillmentUnits
            .FirstOrDefaultAsync(g => g.Id == command.FulfillmentUnitId && g.BrandId == brandId
                                   && g.CurrentBatchId == command.BatchId, cancellationToken);
        if (garment is null) return false;

        // Pass the full ancestor chain (brand → franchise → store/warehouse) so a brand- or
        // franchise-scoped admin matches via an ancestor id rather than 403-ing on the leaf.
        if (!_user.IsWithinScope(brandId: garment.BrandId, franchiseId: garment.FranchiseId, storeId: garment.StoreId, warehouseId: garment.WarehouseId))
            throw new ForbiddenException("This garment is outside your assigned scope.");

        garment.CurrentBatchId = null;
        garment.UpdatedAt      = now;
        garment.UpdatedBy      = command.ActorId;
        garment.Version++;

        var batch = await _db.WarehouseBatches
            .FirstOrDefaultAsync(b => b.Id == command.BatchId && b.BrandId == brandId, cancellationToken);
        if (batch is not null)
        {
            batch.ActualGarmentCount = Math.Max(0, batch.ActualGarmentCount - 1);
            batch.UpdatedAt = now;
            batch.UpdatedBy = command.ActorId;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
