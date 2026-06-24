using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;

namespace operations.Application.Warehouse.Batches.Commands.AddGarmentToBatch;

// ── Add garment to batch ──────────────────────────────────────────────────────

public sealed record AddGarmentToBatchCommand(Guid BatchId, Guid FulfillmentUnitId, Guid? ActorId)
    : ICommand<bool>;

public sealed class AddGarmentToBatchCommandHandler : ICommandHandler<AddGarmentToBatchCommand, bool>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public AddGarmentToBatchCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> HandleAsync(AddGarmentToBatchCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;

        var batch = await _db.WarehouseBatches
            .FirstOrDefaultAsync(b => b.Id == command.BatchId && b.BrandId == brandId, cancellationToken);
        if (batch is null) return false;

        var garment = await _db.FulfillmentUnits
            .FirstOrDefaultAsync(g => g.Id == command.FulfillmentUnitId && g.BrandId == brandId, cancellationToken);
        if (garment is null) return false;

        garment.CurrentBatchId = command.BatchId;
        garment.UpdatedAt      = now;
        garment.UpdatedBy      = command.ActorId;
        garment.Version++;

        batch.ActualGarmentCount++;
        batch.UpdatedAt = now;
        batch.UpdatedBy = command.ActorId;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
