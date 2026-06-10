using FluentValidation;
using laundryghar.Warehouse.Application.Batches.Dtos;
using MediatR;

namespace laundryghar.Warehouse.Application.Batches.Commands;

// ── Create Batch ──────────────────────────────────────────────────────────────

public sealed record CreateWarehouseBatchCommand(CreateWarehouseBatchRequest Request, Guid? ActorId)
    : IRequest<WarehouseBatchDto>;

public sealed class CreateWarehouseBatchHandler
    : IRequestHandler<CreateWarehouseBatchCommand, WarehouseBatchDto>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public CreateWarehouseBatchHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<WarehouseBatchDto> Handle(CreateWarehouseBatchCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var req     = cmd.Request;
        var now     = DateTimeOffset.UtcNow;

        // Validate the warehouse belongs to this brand (cross-brand IDOR guard).
        var warehouseInBrand = await _db.Warehouses
            .AnyAsync(w => w.Id == req.WarehouseId && w.BrandId == brandId, ct);
        if (!warehouseInBrand)
            throw new KeyNotFoundException("Warehouse not found.");

        var count  = await _db.WarehouseBatches.CountAsync(b => b.BrandId == brandId, ct);
        var batchNo = $"WB-{now:yyyyMMdd}-{(count + 1):D4}";

        var batch = new WarehouseBatch
        {
            Id                   = Guid.NewGuid(),
            BrandId              = brandId,
            WarehouseId          = req.WarehouseId,
            BatchNumber          = batchNo,
            BatchType            = req.BatchType,
            ServiceId            = req.ServiceId,
            MachineId            = req.MachineId,
            CycleProgram         = req.CycleProgram,
            ExpectedGarmentCount = req.ExpectedGarmentCount,
            ActualGarmentCount   = 0,
            ChemicalsUsed        = "[]",
            Status               = "created",
            Metadata             = "{}",
            CreatedAt            = now,
            UpdatedAt            = now,
            CreatedBy            = cmd.ActorId,
            UpdatedBy            = cmd.ActorId
        };

        _db.WarehouseBatches.Add(batch);
        await _db.SaveChangesAsync(ct);
        return ToDto(batch);
    }

    internal static WarehouseBatchDto ToDto(WarehouseBatch b) => new(
        b.Id, b.BrandId, b.WarehouseId,
        b.BatchNumber, b.BatchType, b.ServiceId, b.MachineId,
        b.ExpectedGarmentCount, b.ActualGarmentCount,
        b.StartedAt, b.CompletedAt, b.Status, b.FailureReason,
        b.CreatedAt, b.UpdatedAt);
}

// ── Update Batch ──────────────────────────────────────────────────────────────

public sealed record UpdateWarehouseBatchCommand(Guid Id, UpdateWarehouseBatchRequest Request, Guid? ActorId)
    : IRequest<WarehouseBatchDto?>;

public sealed class UpdateWarehouseBatchHandler
    : IRequestHandler<UpdateWarehouseBatchCommand, WarehouseBatchDto?>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateWarehouseBatchHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<WarehouseBatchDto?> Handle(UpdateWarehouseBatchCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var batch = await _db.WarehouseBatches
            .FirstOrDefaultAsync(b => b.Id == cmd.Id && b.BrandId == brandId, ct);
        if (batch is null) return null;

        var now = DateTimeOffset.UtcNow;
        var req = cmd.Request;

        if (req.Status == "running" && batch.StartedAt is null)
            batch.StartedAt = now;
        if (req.Status is "completed" or "failed" or "aborted")
            batch.CompletedAt = now;

        if (req.MachineId is not null) batch.MachineId     = req.MachineId;
        if (req.FailureReason is not null) batch.FailureReason = req.FailureReason;
        batch.Status    = req.Status;
        batch.UpdatedAt = now;
        batch.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return CreateWarehouseBatchHandler.ToDto(batch);
    }
}

// ── Add/Remove garments from batch ────────────────────────────────────────────

public sealed record AddGarmentToBatchCommand(Guid BatchId, Guid GarmentId, Guid? ActorId)
    : IRequest<bool>;

public sealed class AddGarmentToBatchHandler : IRequestHandler<AddGarmentToBatchCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public AddGarmentToBatchHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> Handle(AddGarmentToBatchCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;

        var batch = await _db.WarehouseBatches
            .FirstOrDefaultAsync(b => b.Id == cmd.BatchId && b.BrandId == brandId, ct);
        if (batch is null) return false;

        var garment = await _db.Garments
            .FirstOrDefaultAsync(g => g.Id == cmd.GarmentId && g.BrandId == brandId, ct);
        if (garment is null) return false;

        garment.CurrentBatchId = cmd.BatchId;
        garment.UpdatedAt      = now;
        garment.UpdatedBy      = cmd.ActorId;
        garment.Version++;

        batch.ActualGarmentCount++;
        batch.UpdatedAt = now;
        batch.UpdatedBy = cmd.ActorId;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed record RemoveGarmentFromBatchCommand(Guid BatchId, Guid GarmentId, Guid? ActorId)
    : IRequest<bool>;

public sealed class RemoveGarmentFromBatchHandler : IRequestHandler<RemoveGarmentFromBatchCommand, bool>
{
    private readonly LaundryGharDbContext _db;
    private readonly ICurrentUser _user;

    public RemoveGarmentFromBatchHandler(LaundryGharDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<bool> Handle(RemoveGarmentFromBatchCommand cmd, CancellationToken ct)
    {
        var brandId = _user.RequireBrandId();
        var now     = DateTimeOffset.UtcNow;

        var garment = await _db.Garments
            .FirstOrDefaultAsync(g => g.Id == cmd.GarmentId && g.BrandId == brandId
                                   && g.CurrentBatchId == cmd.BatchId, ct);
        if (garment is null) return false;

        garment.CurrentBatchId = null;
        garment.UpdatedAt      = now;
        garment.UpdatedBy      = cmd.ActorId;
        garment.Version++;

        var batch = await _db.WarehouseBatches
            .FirstOrDefaultAsync(b => b.Id == cmd.BatchId && b.BrandId == brandId, ct);
        if (batch is not null)
        {
            batch.ActualGarmentCount = Math.Max(0, batch.ActualGarmentCount - 1);
            batch.UpdatedAt = now;
            batch.UpdatedBy = cmd.ActorId;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class CreateWarehouseBatchValidator : AbstractValidator<CreateWarehouseBatchCommand>
{
    private static readonly string[] AllowedBatchTypes =
        ["wash_white","wash_color","wash_dark","dry_clean","steam_iron","shoe_clean","specialty","rewash"];

    public CreateWarehouseBatchValidator()
    {
        RuleFor(x => x.Request.WarehouseId).NotEmpty();
        RuleFor(x => x.Request.BatchType)
            .Must(t => AllowedBatchTypes.Contains(t))
            .WithMessage($"BatchType must be one of: {string.Join(", ", AllowedBatchTypes)}.");
        RuleFor(x => x.Request.ExpectedGarmentCount).GreaterThan(0);
    }
}
