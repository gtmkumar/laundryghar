using FluentValidation;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.OrderLifecycle;
using laundryghar.SharedDataModel.Entities.TenancyOrg;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Services;
using Microsoft.EntityFrameworkCore;
using operations.Application.Common.Interfaces;
using operations.Application.Warehouse.Batches.Dtos;

namespace operations.Application.Warehouse.Batches.Commands.CreateWarehouseBatch;

// ── Create Batch ──────────────────────────────────────────────────────────────

public sealed record CreateWarehouseBatchCommand(CreateWarehouseBatchRequest Request, Guid? ActorId)
    : ICommand<WarehouseBatchDto>;

public sealed class CreateWarehouseBatchCommandHandler
    : ICommandHandler<CreateWarehouseBatchCommand, WarehouseBatchDto>
{
    private readonly IOperationsDbContext _db;
    private readonly ICurrentUser _user;

    public CreateWarehouseBatchCommandHandler(IOperationsDbContext db, ICurrentUser user)
    {
        _db   = db;
        _user = user;
    }

    public async Task<WarehouseBatchDto> HandleAsync(CreateWarehouseBatchCommand command, CancellationToken cancellationToken)
    {
        var brandId = _user.RequireBrandId();
        var req     = command.Request;
        var now     = DateTimeOffset.UtcNow;

        // Validate the warehouse belongs to this brand (cross-brand IDOR guard).
        var warehouseInBrand = await _db.Warehouses
            .AnyAsync(w => w.Id == req.WarehouseId && w.BrandId == brandId, cancellationToken);
        if (!warehouseInBrand)
            throw new KeyNotFoundException("Warehouse not found.");

        if (!_user.IsWithinScope(warehouseId: req.WarehouseId))
            throw new ForbiddenException("This warehouse batch is outside your assigned scope.");

        var count  = await _db.WarehouseBatches.CountAsync(b => b.BrandId == brandId, cancellationToken);
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
            CreatedBy            = command.ActorId,
            UpdatedBy            = command.ActorId
        };

        _db.WarehouseBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(batch);
    }

    internal static WarehouseBatchDto ToDto(WarehouseBatch b) => new(
        b.Id, b.BrandId, b.WarehouseId,
        b.BatchNumber, b.BatchType, b.ServiceId, b.MachineId,
        b.ExpectedGarmentCount, b.ActualGarmentCount,
        b.StartedAt, b.CompletedAt, b.Status, b.FailureReason,
        b.CreatedAt, b.UpdatedAt);
}

public sealed class CreateWarehouseBatchValidator : AbstractValidator<CreateWarehouseBatchRequest>
{
    private static readonly string[] AllowedBatchTypes =
        ["wash_white","wash_color","wash_dark","dry_clean","steam_iron","shoe_clean","specialty","rewash"];

    public CreateWarehouseBatchValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.BatchType)
            .Must(t => AllowedBatchTypes.Contains(t))
            .WithMessage($"BatchType must be one of: {string.Join(", ", AllowedBatchTypes)}.");
        RuleFor(x => x.ExpectedGarmentCount).GreaterThan(0);
    }
}
